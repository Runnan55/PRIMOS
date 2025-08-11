// Firebase WebGL Exchange → Firebase Custom Token → Unity
// Requisitos en el HTML antes de este script:
//   <script src="/firebase-app-compat.js"></script>
//   <script src="/firebase-auth-compat.js"></script>
//   <script>window.FIREBASE_CONFIG = { /* tu config */ };</script>

(function () {
    let firebaseApp, firebaseAuth;

    function initFirebase() {
        if (!window.FIREBASE_CONFIG) {
            console.error("[Exchange] Missing FIREBASE_CONFIG.");
            return;
        }
        // Evita doble init si Unity recarga escena/iframe
        try { firebaseApp = firebase.app(); }
        catch { firebaseApp = firebase.initializeApp(window.FIREBASE_CONFIG); }
        firebaseAuth = firebase.auth();
        console.log("[Exchange] Firebase initialized");
    }

    function readExchangeToken() {
        const p = new URLSearchParams(location.search);
        return (
            p.get("exchange_token") || // nombre nuevo preferido
            p.get("code") ||           // compat
            p.get("auth_token")        // compat
        );
    }

    async function exchangeAndLogin(code) {
        const url = `https://account.primos.games/api/exchange?code=${encodeURIComponent(code)}`;
        console.log("[Exchange] GET", url);

        // Nota: no pongas headers ni credentials para evitar preflight
        const res = await fetch(url);
        if (!res.ok) {
            const t = await res.text().catch(() => "");
            throw new Error(`[Exchange] HTTP ${res.status} ${t}`);
        }

        const data = await res.json();
        // Esperamos algo como: { success: true, tokenType: "firebase-custom-token", token: "..." }
        const customToken = data.token || data.customToken;
        if (!customToken) {
            throw new Error("[Exchange] Response without custom token");
        }

        // Login con Custom Token
        const cred = await firebaseAuth.signInWithCustomToken(customToken);
        const user = cred.user || firebaseAuth.currentUser;
        if (!user) throw new Error("[Exchange] Firebase user is null after signIn");

        // Tokens para Unity
        const idToken = await user.getIdToken(true); // fuerza refresco inicial
        const refreshToken = user.refreshToken;

        const payload = {
            uid: user.uid,
            email: user.email || null,
            idToken,
            refreshToken
        };

        // Enviar a Unity (ajusta el nombre del método si tu AuthManager usa otro)
        if (typeof unityInstance !== "undefined") {
            try {
                unityInstance.SendMessage("AuthManager", "OnFirebaseLoginSuccess", JSON.stringify(payload));
            } catch (e) {
                // Compat: si usabas antes otro nombre
                try {
                    unityInstance.SendMessage("AuthManager", "OnFirebaseCredentialsReceived", JSON.stringify(payload));
                } catch (e2) {
                    console.warn("[Exchange] Unity receiver not found:", e2);
                }
            }
        } else {
            console.warn("[Exchange] unityInstance not defined; payload:", payload);
        }

        // Limpia la URL (quita el exchange_token)
        const clean = location.origin + location.pathname;
        window.history.replaceState({}, document.title, clean);
        console.log("[Exchange] Login done, URL cleaned");
    }

    async function run() {
        initFirebase();
        const code = readExchangeToken();
        if (!code) {
            console.log("[Exchange] No exchange_token/code/auth_token in URL. Skipping.");
            return;
        }
        try {
            await exchangeAndLogin(code);
        } catch (err) {
            console.error(err);
            // opcional: alert("No se pudo iniciar sesión. Revisa la consola.");
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", run);
    } else {
        run();
    }
})();
