export async function afterStarted(blazor) {
    const exports = await blazor.runtime.getAssemblyExports('DotNetLab.App.dll');

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
            exports.DotNetLab.Lab.UpdateInfo.UpdateAvailable();
            return;
        }
    
        registration.addEventListener('updatefound', () => {
            exports.DotNetLab.Lab.UpdateInfo.UpdateAvailable();
            return;
        });
    })();
}
