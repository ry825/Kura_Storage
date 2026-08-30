package com.kurastorage.feature.media

import android.content.Context
import android.os.Build
import coil3.ImageLoader
import coil3.disk.DiskCache
import coil3.gif.AnimatedImageDecoder
import coil3.gif.GifDecoder
import coil3.memory.MemoryCache
import com.kurastorage.core.data.media.KuraMediaFetcher
import com.kurastorage.core.data.media.KuraMediaImage
import com.kurastorage.core.data.media.KuraMediaKeyer
import com.kurastorage.core.data.media.MediaRepository
import okio.Path.Companion.toOkioPath
import java.io.File
import kotlin.math.min

object MediaImageLoaderFactory {
    fun create(
        context: Context,
        scopeId: String,
        repository: MediaRepository,
    ): ImageLoader {
        val applicationContext = context.applicationContext
        val memoryBytes = min(Runtime.getRuntime().maxMemory() / MEMORY_HEAP_DIVISOR, MAX_MEMORY_BYTES)
        val diskDirectory = File(applicationContext.cacheDir, "media-images/$scopeId")
        return ImageLoader
            .Builder(applicationContext)
            .memoryCache { MemoryCache.Builder().maxSizeBytes(memoryBytes).build() }
            .diskCache {
                DiskCache
                    .Builder()
                    .directory(diskDirectory.toOkioPath())
                    .maxSizeBytes(MAX_DISK_BYTES)
                    .build()
            }.components {
                add(KuraMediaKeyer, KuraMediaImage::class)
                add(KuraMediaFetcher.Factory(repository), KuraMediaImage::class)
                if (Build.VERSION.SDK_INT >= ANIMATED_IMAGE_DECODER_API) {
                    add(AnimatedImageDecoder.Factory())
                } else {
                    add(GifDecoder.Factory())
                }
            }.build()
    }

    fun cleanupPreviousSessions(context: Context) {
        File(context.applicationContext.cacheDir, "media-images").deleteRecursively()
    }

    fun cleanupSession(
        context: Context,
        scopeId: String,
    ) {
        File(context.applicationContext.cacheDir, "media-images/$scopeId").deleteRecursively()
    }

    private const val MAX_MEMORY_BYTES = 64L * 1024 * 1024
    private const val MAX_DISK_BYTES = 256L * 1024 * 1024
    private const val MEMORY_HEAP_DIVISOR = 10
    private const val ANIMATED_IMAGE_DECODER_API = 28
}
