// Used by CustomSlimWorker.

import '../../js/decode.min.js?v=3';
import '../../js/fetch-interceptor.js?v=3';

const params = new Proxy(new URLSearchParams(self.location.search), {
    get: (searchParams, prop) => searchParams.get(prop),
});
let assembly = params.assembly;
let serializedArgs = params.serializedArgs;

import { dotnet } from "../../_framework/dotnet.js";

const instance = await dotnet.create();

let objects = [];

instance.setModuleImports("boot.js", {
    createObject: () => {
        let newObject = {};
        objects.push(newObject);
        return newObject;
    },
    disposeObject: (obj) => {
        objects = objects.filter(item => item != obj);
    },
    postMessage: (message) => self.postMessage(message),
    registerOnMessage: (handler) => self.addEventListener("message", handler),
});

await instance.runMainAndExit(`${assembly}.wasm`, JSON.parse(serializedArgs));
