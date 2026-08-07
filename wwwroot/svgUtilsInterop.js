export function getSvgElementSize(element) {
    if (!element || typeof element.getBBox !== "function") {
        return null;
    }
    const box = element.getBBox();
    return { width: box.width, height: box.height };
}
