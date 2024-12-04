export function registerEventListeners(dotNetObj) {
    document.addEventListener('keydown', (e) => {
        if (e.ctrlKey && e.key === 's') {
            e.preventDefault();
            dotNetObj.invokeMethodAsync('CompileAndRenderAsync');
        }
    });
}
