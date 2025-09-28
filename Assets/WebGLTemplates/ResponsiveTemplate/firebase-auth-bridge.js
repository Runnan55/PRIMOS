// firebase-auth-bridge.js - Versión CORREGIDA con detección de contexto
(function () {
    let firebaseApp, firebaseAuth;
    let authCheckInterval, authTimeout;
    let pendingAuthData = null;
    let unityCheckInterval = null;

    // Detectar contexto de ejecución
    function guessOpenerOrigin() {
        try {
            const ref = document.referrer || "";
            return ref ? new URL(ref).origin : null;
        } catch { return null; }
    }

    function getContext() {
        if (window.opener && window.opener !== window) {
            const dyn = guessOpenerOrigin();
            return { type: 'popup', target: window.opener, origin: dyn || "https://mini.primos.games" };
        } else if (window.parent && window.parent !== window) {
            const dyn = guessOpenerOrigin();
            return { type: 'iframe', target: window.parent, origin: dyn || "https://mini.primos.games" };
        }
        return { type: 'standalone', target: null, origin: null };
    }

    // 1) Inicializar Firebase con tu window.FIREBASE_CONFIG
    function initFirebase() {
        if (!window.FIREBASE_CONFIG) {
            console.error("[PostMessage] Missing FIREBASE_CONFIG");
            return false;
        }
        try { firebaseApp = firebase.app(); }
        catch { firebaseApp = firebase.initializeApp(window.FIREBASE_CONFIG); }
        firebaseAuth = firebase.auth();
        console.log("[PostMessage] Firebase initialized");
        return true;
    }

    // 2) Pedir custom token al parent/opener según contexto
    function requestAuthToken() {
        const context = getContext();

        if (!context.target) {
            console.error("[PostMessage] No parent/opener found - running standalone");
            return;
        }

        console.log(`[PostMessage] Requesting auth token via ${context.type} to ${context.origin}`);
        const msg = {
            type: "REQUEST_AUTH_TOKEN",
            gameId: "mini-primos",
            timestamp: Date.now()
        };

        context.target.postMessage(msg, context.origin);
    }

    // 3) Recibir respuesta con token y login Firebase
    async function handleAuthResponse(event) {
        // Seguridad: solo aceptar desde account.primos.games
        const allowed = new Set([
            guessOpenerOrigin(),
            "https://mini.primos.games",
            "https://account.primos.games"
        ]);
        if (!allowed.has(event.origin)) {
            console.warn("[PostMessage] Ignored message from:", event.origin);
            return;
        }

        if (event.data?.type !== "AUTH_TOKEN_RESPONSE") return;

        console.log("[PostMessage] Received auth response");

        // Limpiar intervalo de reintentos
        clearAuthCheck();

        if (!event.data.success) {
            console.error("[PostMessage] Auth failed:", event.data.error);
            notifyUnity("AUTH_FAILED", { error: event.data.error });
            return;
        }

        try {
            const customToken = event.data.token;
            const cred = await firebaseAuth.signInWithCustomToken(customToken);
            const user = cred.user;

            console.log("[PostMessage] Firebase auth successful:", user.uid);

            // Tokens para Unity
            const idToken = await user.getIdToken(true);
            const refreshToken = user.refreshToken;

            const payload = {
                uid: user.uid,
                email: user.email || null,
                idToken,
                refreshToken
            };

            // Intentar notificar a Unity
            notifyUnity("AUTH_SUCCESS", payload);

        } catch (err) {
            console.error("[PostMessage] Firebase auth error:", err);
            notifyUnity("AUTH_FAILED", { error: err.message });
        }
    }

    // 4) Pasar resultado a Unity con reintentos
    function notifyUnity(status, data) {
        // Guardar datos por si Unity no está listo
        pendingAuthData = { status, data };

        // Intentar enviar inmediatamente
        if (tryNotifyUnity()) {
            console.log("[PostMessage] Unity notified successfully");
            pendingAuthData = null;
            return;
        }

        // Si no está listo, iniciar reintentos
        console.log("[PostMessage] Unity not ready, starting retry loop...");

        let retryCount = 0;
        const maxRetries = 60; // 30 segundos con intervalos de 500ms

        unityCheckInterval = setInterval(() => {
            retryCount++;

            if (tryNotifyUnity()) {
                console.log(`[PostMessage] Unity notified after ${retryCount} retries`);
                clearInterval(unityCheckInterval);
                pendingAuthData = null;
                return;
            }

            if (retryCount >= maxRetries) {
                console.error("[PostMessage] Unity notification timeout after 30 seconds");
                clearInterval(unityCheckInterval);
                // Mantener pendingAuthData por si Unity aparece más tarde
            }
        }, 500);
    }

    // Intentar notificar a Unity (devuelve true si exitoso)
    function tryNotifyUnity() {
        if (!pendingAuthData) return false;

        if (typeof unityInstance === "undefined" || !unityInstance) {
            return false;
        }

        try {
            const { status, data } = pendingAuthData;

            if (status === "AUTH_SUCCESS") {
                unityInstance.SendMessage("AuthManager", "OnFirebaseLoginSuccess", JSON.stringify(data));
                console.log("[PostMessage] Sent AUTH_SUCCESS to Unity");
            } else {
                unityInstance.SendMessage("AuthManager", "OnFirebaseLoginError", data.error || "Unknown error");
                console.log("[PostMessage] Sent AUTH_FAILED to Unity");
            }

            return true;
        } catch (e) {
            console.warn("[PostMessage] Error sending to Unity (will retry):", e);
            return false;
        }
    }

    // 5) Limpieza
    function clearAuthCheck() {
        if (authCheckInterval) {
            clearInterval(authCheckInterval);
            authCheckInterval = null;
        }
        if (authTimeout) {
            clearTimeout(authTimeout);
            authTimeout = null;
        }
    }

    // 6) Bootstrap
    function init() {
        if (!initFirebase()) {
            console.error("[PostMessage] Failed to initialize Firebase");
            return;
        }

        // Detectar contexto
        const context = getContext();
        console.log(`[PostMessage] Running in ${context.type} mode`);

        // Solo proceder si hay parent/opener
        if (!context.target) {
            console.warn("[PostMessage] No parent/opener - waiting for manual auth");
            return;
        }

        // Escuchar mensajes
        window.addEventListener('message', handleAuthResponse);

        // Solicitar token inmediatamente
        requestAuthToken();

        // Reintentar cada 2 segundos por si el parent no está listo
        authCheckInterval = setInterval(requestAuthToken, 2000);

        // Timeout después de 30 segundos
        authTimeout = setTimeout(() => {
            clearAuthCheck();
            console.error("[PostMessage] Auth timeout - no response received");
            notifyUnity('AUTH_FAILED', { error: 'Authentication timeout' });
        }, 30000);
    }

    // Exponer función para que Unity pueda reintentar
    window.retryAuthentication = function () {
        clearAuthCheck();
        init();
    };

    // Exponer función para chequear si hay auth pendiente
    window.checkPendingAuth = function () {
        if (pendingAuthData) {
            console.log("[PostMessage] Retrying pending auth data...");
            tryNotifyUnity();
        }
    };

    // Iniciar cuando el DOM esté listo
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();