function fileOpenAs() {
    var msg = "exit";

    if ('launchQueue' in window) {
        console.debug('[FRAMLab] File handling API is supported.');
        launchQueue.setConsumer((launchParams) => {
            // Nothing to do when the queue is empty.
            if (!launchParams.files.length) {
                msg = "empty";
                return msg;
            }
            // Handle the first file only.
            fileContentAs(launchParams.files[0]);
        });
        return msg;
    } else {
        console.debug('[FRAMLab] File handling API is not supported.');
        return "not supported";
    }
    async function fileContentAs(fileHandle) {
        try {
            msg = "waiting";
            const blob = await fileHandle.getFile();
            blob.handle = fileHandle;
            const content = await blob.text();

            DotNet.invokeMethodAsync('FMV_OG', 'FileContent', content, fileHandle.name);
        } catch (e) {
            console.error('[FRAMLab] Could not read the file handed over by the operating system.', e);
        }
    }
}