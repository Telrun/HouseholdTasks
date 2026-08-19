// This file MUST live at the site root (not under /js) and be named exactly
// firebase-messaging-sw.js — that's the default path Firebase's SDK looks for when
// registering a service worker for background push. It runs in its own worker context,
// separate from the page, which is why it re-imports Firebase and re-initializes it here
// even though index.html already does the same thing for the foreground app.
//
// This also doubles as the app's general PWA service worker (registered from
// notifications.js) — deliberately kept minimal on the caching side so it doesn't fight
// with Blazor WebAssembly's own asset versioning/integrity-hash caching.

importScripts("https://www.gstatic.com/firebasejs/10.12.0/firebase-app-compat.js");
importScripts("https://www.gstatic.com/firebasejs/10.12.0/firebase-messaging-compat.js");

// TODO: replace with your actual Firebase project config (Project Settings > General >
// Your apps > SDK setup and configuration). These values are meant to be public — they
// identify your project, they aren't secrets — so it's normal/expected for them to sit in
// a plain static file like this.
firebase.initializeApp({
    apiKey: "AIzaSyB0DOcbt_1Mi2G_zps2wuCv9lwxSczDAog",
    authDomain: "oppgavernotify.firebaseapp.com",
    projectId: "oppgavernotify",
    storageBucket: "oppgavernotify.firebasestorage.app",
    messagingSenderId: "163819937270",
    appId: "1:163819937270:web:5920f07401f820176d3bd7"
});

const messaging = firebase.messaging();

// Fires when a push arrives while no tab has focus (app closed, phone locked, etc.) — the
// one case where FCM won't display a notification for us automatically on web, unlike
// native mobile push, so we build it explicitly.
messaging.onBackgroundMessage((payload) => {
    const title = payload.notification?.title ?? payload.data?.title ?? "Husoppgaver";
    const body = payload.notification?.body ?? payload.data?.body ?? "";

    self.registration.showNotification(title, {
        body,
        icon: "icons/icon-192.png",
        badge: "icons/icon-192.png",
        data: { url: payload.data?.url ?? "/my-tasks" }
    });
});

// Tapping the notification should open (or focus) the app, not just dismiss it.
self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    const targetUrl = event.notification.data?.url ?? "/my-tasks";

    event.waitUntil(
        clients.matchAll({ type: "window", includeUncontrolled: true }).then((windowClients) => {
            for (const client of windowClients) {
                if (client.url.includes(targetUrl) && "focus" in client) {
                    return client.focus();
                }
            }
            if (clients.openWindow) {
                return clients.openWindow(targetUrl);
            }
        })
    );
});

// Minimal PWA installability requirement: a fetch handler needs to exist, but we
// deliberately don't do custom caching here — just pass every request straight through to
// the network, and let the browser's normal HTTP cache (plus Blazor's own asset
// versioning) handle the rest.
self.addEventListener("fetch", () => {});
