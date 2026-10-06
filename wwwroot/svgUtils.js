window.SvgUtils = {
    getSvgElementSize: function (element) {
        if (!element) {
            console.warn("[FRAMLab] SvgUtils.getSvgElementSize called without an element.");
            return null;
        }

        const bbox = element.getBBox();
        const ctm = element.getCTM();

        if (!bbox || !ctm) {
            console.warn("[FRAMLab] SvgUtils.getSvgElementSize: element has no bounding box/CTM (not laid out yet?).");
            return null;
        }

        return {
            width: bbox.width * ctm.a,
            height: bbox.height * ctm.d
        };
    }
};
