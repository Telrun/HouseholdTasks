// JS interop surface for push notifications, called from HouseholdTasks.Client's
// NotificationService.cs via IJSRuntime. Kept as plain global functions (window.hhNotify.*)
// rather than an ES module, since that's the simplest thing to call from Blazor without
// wrestling with dynamic import() semantics.

window.hhNotify = (function () {
    // TODO: same project config as firebase-messaging-sw.js — must match exactly.
    const firebaseConfig = {
        apiKey: "AIzaSyB0DOcbt_1Mi2G_zps2wuCv9lwxSczDAog",
        authDomain: "oppgavernotify.firebaseapp.com",
        projectId: "oppgavernotify",
        storageBucket: "oppgavernotify.firebasestorage.app",
        messagingSenderId: "163819937270",
        appId: "1:163819937270:web:5920f07401f820176d3bd7"
    };

    // TODO: from Firebase Console -> Project Settings -> Cloud Messaging -> Web Push
    // certificates -> Key pair. Public key, safe to ship client-side.
    const vapidKey = "BKBKQ3n7PjwHfpDwfywVhQkl42zsBThkKrJn7JkD9Wvme-kBiLFDQtdpEivsS5Bl5-85kMfR2AkXsrGWE2HdcQY";

    let app = null;
    let messaging = null;
    let swRegistration = null;
    let dotNetForegroundHandler = null;

    function ensureInitialized() {
        if (!app) {
            app = firebase.initializeApp(firebaseConfig);
            messaging = firebase.messaging(app);
        }
    }

    // iOS's Push API only exists for a site that's been added to the Home Screen and is
    // currently running in that standalone context — never for a regular Safari (or any
    // other iOS browser, since they're all WebKit under the hood) tab, no matter what
    // permissions are granted. Android/desktop have no such restriction.
    function isIos() {
        return /iphone|ipad|ipod/i.test(navigator.userAgent);
    }

    function isStandalone() {
        return window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
    }

    return {
        // Called once at app startup (see NotificationService.cs) purely to register the
        // service worker for PWA installability — independent of whether the user ever
        // enables notifications.
        registerServiceWorker: async function () {
            if (!("serviceWorker" in navigator)) return false;
            try {
                swRegistration = await navigator.serviceWorker.register("/firebase-messaging-sw.js");
                return true;
            } catch (e) {
                console.error("Service worker registration failed:", e);
                return false;
            }
        },

        // Returns a status string the Blazor side can branch on, rather than throwing —
        // "ios-needs-install" is the one the UI most needs to react to specially.
        getNotificationStatus: function () {
            if (!("Notification" in window) || !("serviceWorker" in navigator)) return "unsupported";
            if (isIos() && !isStandalone()) return "ios-needs-install";
            if (Notification.permission === "denied") return "denied";
            if (Notification.permission === "granted") return "granted";
            return "not-requested";
        },

        // Requests permission (if needed) and returns an FCM token for this device, or
        // null if permission was refused / something failed. The caller is responsible for
        // POSTing the token to the server.
        requestPermissionAndGetToken: async function () {
            try {
                ensureInitialized();

                if (Notification.permission !== "granted") {
                    const permission = await Notification.requestPermission();
                    if (permission !== "granted") return null;
                }

                if (!swRegistration) {
                    swRegistration = await navigator.serviceWorker.register("/firebase-messaging-sw.js");
                }

                const token = await firebase.messaging(app).getToken({
                    vapidKey: vapidKey,
                    serviceWorkerRegistration: swRegistration
                });
                return token ?? null;
            } catch (e) {
                console.error("Failed to get FCM token:", e);
                return null;
            }
        },

        // Foreground messages (app tab open and focused) don't trigger the service
        // worker's onBackgroundMessage — Firebase leaves it entirely to the app to decide
        // how to show these, so we hand it back to Blazor to render as an in-app toast.
        listenForForegroundMessages: function (dotNetHelper) {
            ensureInitialized();
            dotNetForegroundHandler = dotNetHelper;

            firebase.messaging(app).onMessage((payload) => {
                const title = payload.notification?.title ?? payload.data?.title ?? "";
                const body = payload.notification?.body ?? payload.data?.body ?? "";
                dotNetForegroundHandler.invokeMethodAsync("OnForegroundNotification", title, body);
            });
        }
    };
})();
