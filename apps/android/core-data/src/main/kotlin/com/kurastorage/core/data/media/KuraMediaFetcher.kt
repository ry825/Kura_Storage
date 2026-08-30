package com.kurastorage.core.data.media

import coil3.ImageLoader
import coil3.decode.DataSource
import coil3.decode.ImageSource
import coil3.fetch.FetchResult
import coil3.fetch.Fetcher
import coil3.fetch.SourceFetchResult
import coil3.key.Keyer
import coil3.request.Options
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import okio.FileSystem

data class KuraMediaImage(
    val scopeId: String,
    val fileId: String,
    val fileVersion: Long,
    val variant: MediaVariant,
    val requestGeneration: Int = 0,
) {
    init {
        require(scopeId.isNotBlank())
        require(fileId.isNotBlank())
        require(fileVersion >= 0)
        require(requestGeneration >= 0)
    }

    val cacheKey: String get() = "$scopeId:$fileId:$fileVersion:${variant.wireValue}"
}

class MediaGeneratingException(
    val job: MediaJobSnapshot,
) : IllegalStateException("Media derivative is being generated")

object KuraMediaKeyer : Keyer<KuraMediaImage> {
    override fun key(
        data: KuraMediaImage,
        options: Options,
    ): String = data.cacheKey
}

class KuraMediaFetcher internal constructor(
    private val data: KuraMediaImage,
    private val repository: MediaRepository,
    private val permits: Semaphore,
) : Fetcher {
    override suspend fun fetch(): FetchResult =
        permits.withPermit {
            when (val result = repository.openContent(data.fileId, data.variant)) {
                is MediaContentResult.Generating -> throw MediaGeneratingException(result.job)
                is MediaContentResult.Ready -> {
                    val content = result.content
                    SourceFetchResult(
                        source = ImageSource(content.body.source(), FileSystem.SYSTEM),
                        mimeType = content.headers["Content-Type"]?.substringBefore(';')?.trim(),
                        dataSource = DataSource.NETWORK,
                    )
                }
            }
        }

    class Factory(
        private val repository: MediaRepository,
        maximumParallelRequests: Int = DEFAULT_MAXIMUM_PARALLEL_REQUESTS,
    ) : Fetcher.Factory<KuraMediaImage> {
        private val permits = Semaphore(maximumParallelRequests)

        init {
            require(maximumParallelRequests in 1..DEFAULT_MAXIMUM_PARALLEL_REQUESTS)
        }

        override fun create(
            data: KuraMediaImage,
            options: Options,
            imageLoader: ImageLoader,
        ): Fetcher = KuraMediaFetcher(data, repository, permits)
    }

    private companion object {
        const val DEFAULT_MAXIMUM_PARALLEL_REQUESTS = 8
    }
}
