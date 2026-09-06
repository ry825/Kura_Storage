package com.kurastorage.feature.media.photo

data class PhotoRenderSize(
    val widthPx: Float,
    val heightPx: Float,
)

object PhotoFitPolicy {
    fun fitWithoutUpscaling(
        intrinsicWidthPx: Float,
        intrinsicHeightPx: Float,
        viewportWidthPx: Float,
        viewportHeightPx: Float,
    ): PhotoRenderSize {
        require(intrinsicWidthPx > 0f && intrinsicHeightPx > 0f)
        require(viewportWidthPx > 0f && viewportHeightPx > 0f)
        val scale =
            minOf(
                viewportWidthPx / intrinsicWidthPx,
                viewportHeightPx / intrinsicHeightPx,
                1f,
            )
        return PhotoRenderSize(intrinsicWidthPx * scale, intrinsicHeightPx * scale)
    }

    fun maximumPanPx(
        renderedSizePx: Float,
        viewportSizePx: Float,
        zoom: Float,
    ): Float = ((renderedSizePx * zoom - viewportSizePx) / 2f).coerceAtLeast(0f)
}
