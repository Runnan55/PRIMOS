// Firebase Auth Bridge for Unity WebGL
// Este archivo debe ser incluido en el HTML template de Unity WebGL

// Importar Firebase Auth (debe ser incluido antes que este script)
// <script src="https://www.gstatic.com/firebasejs/10.12.2/firebase-app.js"></script>
// <script src="https://www.gstatic.com/firebasejs/10.12.2/firebase-auth.js"></script>
// <script src="https://account.primos.games/api/config/firebase"></script>

// Variables globales de Firebase
let firebaseApp;
let firebaseAuth;
let firebaseConfig;

// Función para obtener configuración de Firebase desde el servidor
function getFirebaseConfig() {
  if (!window.FIREBASE_CONFIG) {
    throw new Error('Firebase configuration not loaded. Make sure to include the config script from account.primos.games');
  }
  
  return window.FIREBASE_CONFIG;
}

// Función para validar configuración
function validateFirebaseConfig(config) {
  const requiredKeys = ['apiKey', 'authDomain', 'projectId', 'storageBucket', 'messagingSenderId', 'appId'];
  const missingKeys = requiredKeys.filter(key => !config[key]);
  
  if (missingKeys.length > 0) {
    console.error('Firebase Auth Bridge: Configuración incompleta. Variables faltantes:', missingKeys);
    console.error('Firebase Auth Bridge: La configuración se carga desde account.primos.games');
    throw new Error(`Firebase configuration missing: ${missingKeys.join(', ')}`);
  }
  
  return true;
}

// Función para inicializar Firebase
function initializeFirebase() {
  try {
    firebaseConfig = getFirebaseConfig();
    validateFirebaseConfig(firebaseConfig);
    
    firebaseApp = firebase.initializeApp(firebaseConfig);
    firebaseAuth = firebase.auth();
    
    console.log('Firebase Auth Bridge: Inicialización exitosa');
    console.log('Firebase Auth Bridge: Project ID:', firebaseConfig.projectId);
    
    return true;
  } catch (error) {
    console.error('Firebase Auth Bridge: Error en inicialización:', error);
    return false;
  }
}

// Fuera de AuthBridge
async function refreshAndStoreToken() {
    try {
        const token = await firebaseAuth.currentUser.getIdToken(true); // Fuerza refresco
        localStorage.setItem("jwt_token", token);
        console.log("[Bridge] Token refrescado y guardado en localStorage");
    } catch (error) {
        console.error("[Bridge] Error al refrescar y guardar el token:", error);
    }
}


// Estado del bridge
const AuthBridge = {
  isInitialized: false,
  currentUser: null,
  onAuthStateChangedCallbacks: [],
  
  // Función para obtener el token desde la URL
  getTokenFromURL() {
    const urlParams = new URLSearchParams(window.location.search);
    const token = urlParams.get('auth_token');
    
    if (token) {
      console.log('Firebase Auth Bridge: Token encontrado en URL');
      // Limpiar el token de la URL por seguridad
      const url = new URL(window.location);
      url.searchParams.delete('auth_token');
      window.history.replaceState({}, document.title, url.toString());
      return token;
    }
    
    return null;
  },

  // Función principal de autenticación con custom token
  async signInWithCustomToken(customToken) {
    try {
      console.log('Firebase Auth Bridge: Iniciando autenticación con custom token');
      
      const userCredential = await firebaseAuth.signInWithCustomToken(customToken);
      const user = userCredential.user;
      
      // Obtener el ID token para futuras llamadas a la API
        const idToken = await user.getIdToken();
        localStorage.setItem("jwt_token", idToken);
        await refreshAndStoreToken(); // Refresca y guarda nuevamente para garantizar validez

      
      // Datos del usuario para Unity
      const userData = {
        uid: user.uid,
        email: user.email,
        emailVerified: user.emailVerified,
        displayName: user.displayName,
        photoURL: user.photoURL,
        // Custom claims se obtienen del token decodificado
        customClaims: await this.getCustomClaims(idToken)
      };
      
      this.currentUser = userData;
      console.log('Firebase Auth Bridge: Autenticación exitosa', userData);
      
      // Notificar a Unity sobre el éxito
      this.notifyUnity('onAuthSuccess', userData);
      
      return {
        success: true,
        user: userData,
        idToken: idToken
      };
      
    } catch (error) {
      console.error('Firebase Auth Bridge: Error en autenticación:', error);
      
      // Notificar a Unity sobre el error
      this.notifyUnity('onAuthError', {
        code: error.code,
        message: error.message
      });
      
      return {
        success: false,
        error: {
          code: error.code,
          message: error.message
        }
      };
      }
  },
  
  // Obtener custom claims del ID token
  async getCustomClaims(idToken) {
    try {
      // Decodificar el payload del JWT (solo para lectura, no para validación)
      const payload = JSON.parse(atob(idToken.split('.')[1]));
      
      return {
        role: payload.role || 'user',
        walletAddress: payload.walletAddress || null,
        hasNFT: payload.hasNFT || false,
        nftVerifiedAt: payload.nftVerifiedAt || null,
        gameUrl: payload.gameUrl || null
      };
    } catch (error) {
      console.error('Firebase Auth Bridge: Error obteniendo custom claims:', error);
      return {};
    }
  },
  
  // Validar token actual
  async validateCurrentToken() {
    try {
      if (!firebaseAuth.currentUser) {
        return { valid: false, error: 'No hay usuario autenticado' };
      }
      
      const idToken = await firebaseAuth.currentUser.getIdToken(true); // Force refresh
      
      // Validar con el servidor
      const accountUrl = window.ACCOUNT_URL || 'https://account.primos.games';
      const response = await fetch(`${accountUrl}/api/token/validate`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ token: idToken })
      });
      
      const result = await response.json();
      
      if (result.valid) {
        console.log('Firebase Auth Bridge: Token válido');
        return { valid: true, user: result.user };
      } else {
        console.error('Firebase Auth Bridge: Token inválido:', result.error);
        return { valid: false, error: result.error };
      }
      
    } catch (error) {
      console.error('Firebase Auth Bridge: Error validando token:', error);
      return { valid: false, error: error.message };
    }
  },
  
  // Obtener nuevo ID token
  async getIdToken(forceRefresh = false) {
    try {
      if (!firebaseAuth.currentUser) {
        throw new Error('No hay usuario autenticado');
      }
      
      return await firebaseAuth.currentUser.getIdToken(forceRefresh);
    } catch (error) {
      console.error('Firebase Auth Bridge: Error obteniendo ID token:', error);
      throw error;
    }
  },
  
  // Cerrar sesión
  async signOut() {
    try {
      await firebaseAuth.signOut();
      this.currentUser = null;
      console.log('Firebase Auth Bridge: Sesión cerrada');
      
      // Notificar a Unity
      this.notifyUnity('onSignOut', {});
      
      return { success: true };
    } catch (error) {
      console.error('Firebase Auth Bridge: Error cerrando sesión:', error);
      return { success: false, error: error.message };
    }
  },
  
  // Listener para cambios en el estado de autenticación
  onAuthStateChanged(callback) {
    this.onAuthStateChangedCallbacks.push(callback);
    
    // Si ya hay un usuario, llamar el callback inmediatamente
    if (this.currentUser) {
      callback(this.currentUser);
    }
  },
  
  // Notificar a Unity usando sendMessage
  notifyUnity(methodName, data) {
    try {
      if (typeof unityInstance !== 'undefined' && unityInstance) {
        // Formato estándar para Unity WebGL
        unityInstance.SendMessage('AuthManager', methodName, JSON.stringify(data));
      } else if (typeof gameInstance !== 'undefined' && gameInstance) {
        // Formato alternativo
        gameInstance.SendMessage('AuthManager', methodName, JSON.stringify(data));
      } else {
        console.warn('Firebase Auth Bridge: Unity instance no encontrada');
        // Fallback: dispatch evento personalizado
        window.dispatchEvent(new CustomEvent(`unity_${methodName}`, { detail: data }));
      }
    } catch (error) {
      console.error('Firebase Auth Bridge: Error notificando a Unity:', error);
    }
  },
  
  // Inicialización automática
  async initialize() {
    try {
      console.log('Firebase Auth Bridge: Iniciando inicialización');
      
      // Inicializar Firebase con la configuración cargada
      if (!initializeFirebase()) {
        throw new Error('No se pudo inicializar Firebase');
      }
      
      // Configurar listener de cambios de autenticación
      firebaseAuth.onAuthStateChanged((user) => {
        if (user) {
          console.log('Firebase Auth Bridge: Usuario autenticado detectado');
          this.currentUser = {
            uid: user.uid,
            email: user.email,
            emailVerified: user.emailVerified,
            displayName: user.displayName,
            photoURL: user.photoURL
          };
          
          // Notificar a todos los callbacks
          this.onAuthStateChangedCallbacks.forEach(callback => {
            try {
              callback(this.currentUser);
            } catch (error) {
              console.error('Firebase Auth Bridge: Error en callback:', error);
            }
          });
        } else {
          console.log('Firebase Auth Bridge: Usuario no autenticado');
          this.currentUser = null;
        }
      });
      
      // Intentar autenticación automática si hay token en URL
      const token = this.getTokenFromURL();
      if (token) {
        console.log('Firebase Auth Bridge: Intentando autenticación automática');
        await this.signInWithCustomToken(token);
      }
      
      this.isInitialized = true;
      console.log('Firebase Auth Bridge: Inicialización completada');
      
      // Notificar a Unity que el bridge está listo
      this.notifyUnity('onBridgeReady', { initialized: true });
      
    } catch (error) {
      console.error('Firebase Auth Bridge: Error en inicialización:', error);
      this.notifyUnity('onBridgeError', { error: error.message });
    }
  }
};

// Exponer el bridge globalmente para que Unity pueda acceder
window.FirebaseAuthBridge = AuthBridge;

// Funciones globales para Unity (compatibilidad)
window.signInWithCustomToken = (token) => AuthBridge.signInWithCustomToken(token);
window.validateCurrentToken = () => AuthBridge.validateCurrentToken();
window.getIdToken = (forceRefresh) => AuthBridge.getIdToken(forceRefresh);
window.signOut = () => AuthBridge.signOut();

// Función para inicializar cuando la configuración esté lista
function initializeBridge() {
  if (window.FIREBASE_CONFIG) {
    // Configuración ya cargada, inicializar inmediatamente
    AuthBridge.initialize();
  } else {
    // Esperar a que se cargue la configuración
    const checkConfig = () => {
      if (window.FIREBASE_CONFIG) {
        AuthBridge.initialize();
      } else {
        // Retry después de 100ms
        setTimeout(checkConfig, 100);
      }
    };
    
    checkConfig();
    
    // Fallback: si no se carga la configuración en 10 segundos, mostrar error
    setTimeout(() => {
      if (!window.FIREBASE_CONFIG) {
        console.error('Firebase Auth Bridge: Timeout esperando configuración de Firebase');
        console.error('Firebase Auth Bridge: Asegúrate de incluir el script de configuración de account.primos.games');
      }
    }, 10000);
  }
}

// Esperar explícitamente a que Unity esté listo
function waitForUnityInstance(callback) {
    const check = () => {
        if (window.unityInstance && typeof window.unityInstance.SendMessage === "function") {
            callback();
        } else {
            setTimeout(check, 100); // intenta cada 100ms
        }
    };
    check();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        waitForUnityInstance(initializeBridge);
    });
} else {
    waitForUnityInstance(initializeBridge);
}


console.log('Firebase Auth Bridge: Script cargado - esperando configuración del servidor...');
