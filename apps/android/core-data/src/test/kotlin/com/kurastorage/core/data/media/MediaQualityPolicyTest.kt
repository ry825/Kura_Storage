package com.kurastorage.core.data.media

import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaQualityPolicyTest {
    @Test
    fun `resolver returns all four contexts and fails unknown transport closed`() =
        runTest {
            assertEquals(
                NetworkQualityContext.LOCAL_DIRECT,
                NetworkQualityContextResolver(FakeTransport(NetworkTransport.WIFI), FakeWifi(true))
                    .resolve(ConnectionRoute.LOCAL_DIRECT),
            )
            assertEquals(
                NetworkQualityContext.REGISTERED_REMOTE_WIFI,
                NetworkQualityContextResolver(FakeTransport(NetworkTransport.WIFI), FakeWifi(true))
                    .resolve(ConnectionRoute.REMOTE_SECURE),
            )
            assertEquals(
                NetworkQualityContext.UNREGISTERED_REMOTE_WIFI,
                NetworkQualityContextResolver(FakeTransport(NetworkTransport.WIFI), FakeWifi(false))
                    .resolve(ConnectionRoute.REMOTE_SECURE),
            )
            assertEquals(
                NetworkQualityContext.REMOTE_MOBILE,
                NetworkQualityContextResolver(FakeTransport(NetworkTransport.CELLULAR), FakeWifi(false))
                    .resolve(ConnectionRoute.REMOTE_SECURE),
            )
            assertEquals(
                NetworkQualityContext.UNREGISTERED_REMOTE_WIFI,
                NetworkQualityContextResolver(FakeTransport(NetworkTransport.UNKNOWN), FakeWifi(true))
                    .resolve(ConnectionRoute.REMOTE_SECURE),
            )
        }

    @Test
    fun `preference codec falls back per environment and never removes manual options`() {
        val decoded =
            QualityPreferencesCodec.decode(
                mapOf(
                    NetworkQualityContext.LOCAL_DIRECT to "FUTURE",
                    NetworkQualityContext.REGISTERED_REMOTE_WIFI to "LOW",
                ),
            )
        assertEquals(MediaQuality.ORIGINAL, decoded.localDirect)
        assertEquals(MediaQuality.LOW, decoded.registeredRemoteWifi)
        assertEquals(MediaQuality.LOW, decoded.remoteMobile)
        assertEquals(MediaQuality.entries, QualityPreferencesCodec.manualChoices)
    }

    @Test
    fun `transfer approval is bound to file version variant and observed size`() =
        runTest {
            val repository = MetadataOnlyRepository()
            val policy = TransferConfirmationPolicy(repository)

            val prompt = policy.prepare("file", 4, MediaKind.VIDEO)
            val approval = prompt.approve()

            assertEquals("1.5 MiB", prompt.formattedSize)
            assertTrue(prompt.description.contains("range playback may use less"))
            assertTrue(approval.matches("file", 4, MediaVariant.ORIGINAL, ByteCount(1_572_864)))
            assertFalse(approval.matches("file", 5, MediaVariant.ORIGINAL, ByteCount(1_572_864)))
            assertFalse(approval.matches("file", 4, MediaVariant.VIDEO_LOW, ByteCount(1_572_864)))
            assertEquals(0, repository.contentRequests)
        }

    @Test
    fun `unknown size still requires explicit approval without content prefetch`() =
        runTest {
            val repository =
                MetadataOnlyRepository().apply {
                    metadataFailure = KuraStorageException.InvalidServerResponse()
                }
            val prompt = TransferConfirmationPolicy(repository).prepare("file", 4, MediaKind.AUDIO)

            assertEquals(null, prompt.size)
            assertEquals("Size unavailable", prompt.formattedSize)
            assertTrue(prompt.description.contains("only after you confirm"))
            assertEquals(0, repository.contentRequests)
            assertTrue(prompt.approve().matches("file", 4, MediaVariant.ORIGINAL, null))
        }

    private class FakeTransport(
        private val transport: NetworkTransport,
    ) : NetworkTransportSource {
        override fun activeTransport() = transport
    }

    private class FakeWifi(
        private val registered: Boolean,
    ) : RegisteredWifiSource {
        override suspend fun isRegistered() = registered
    }

    private class MetadataOnlyRepository : MediaRepository {
        var contentRequests = 0
        var metadataFailure: KuraStorageException? = null

        override suspend fun inspectOriginal(fileId: String): OriginalMetadata {
            metadataFailure?.let { throw it }
            return OriginalMetadata(ByteCount(1_572_864), "video/mp4", true)
        }

        override suspend fun job(jobId: String) = error("unused")

        override suspend fun retryJob(jobId: String) = error("unused")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            contentRequests++
            error("must not prefetch")
        }
    }
}
