export async function afterStarted(blazor) {
    const exports = await blazor.runtime.getAssemblyExports('DotNetInternals.App.dll');

    // Check whether service worker has an update available.
    (async () => {
        if (location.hostname === 'localhost') {
            return;
        }

        const registration = await navigator.serviceWorker.getRegistration();
        if (!registration) {
            return;
        }
    
        if (registration.waiting) {
            exports.DotNetInternals.Lab.UpdateInfo.UpdateAvailable();
            return;
        }
    
        registration.addEventListener('updatefound', () => {
            exports.DotNetInternals.Lab.UpdateInfo.UpdateAvailable();
            return;
        });
    })();
}
