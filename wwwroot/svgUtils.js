window.SvgUtils = {
    getSvgElementSize: function (element) {
        if (!element) {
            return null;
        }

        const bbox = element.getBBox();
        const ctm = element.getCTM();

        if (!bbox || !ctm) {
            return null;
        }

        return {
            width: bbox.width * ctm.a,
            height: bbox.height * ctm.d
        };
    }
};
