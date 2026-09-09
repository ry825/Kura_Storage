package com.kurastorage.feature.media.player

import androidx.media3.common.PlaybackException
import com.kurastorage.core.data.media.MediaDataSourceIOException
import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.IOException

class PlaybackFailureClassificationTest {
    @Test
    fun codecDecodeErrorsAreDistinctFromTransportAndContentFailures() {
        assertEquals(
            PlayerFailure.UNSUPPORTED_CODEC,
            classifyPlaybackFailure(exception(PlaybackException.ERROR_CODE_DECODING_FORMAT_UNSUPPORTED)),
        )
        assertEquals(
            PlayerFailure.DECODER,
            classifyPlaybackFailure(exception(PlaybackException.ERROR_CODE_DECODER_INIT_FAILED)),
        )
        assertEquals(PlayerFailure.RANGE, classifyPlaybackFailure(exception(cause = MediaDataSourceIOException.InvalidRange)))
        assertEquals(PlayerFailure.INCOMPLETE, classifyPlaybackFailure(exception(cause = MediaDataSourceIOException.Incomplete)))
        assertEquals(
            PlayerFailure.NETWORK,
            classifyPlaybackFailure(exception(cause = MediaDataSourceIOException.Network(IOException("disconnect")))),
        )
    }

    @Test
    fun authenticationAndAuthorizationFailuresRemainDistinctFromCodecFailure() {
        assertEquals(
            PlayerFailure.AUTHENTICATION,
            classifyPlaybackFailure(exception(cause = MediaDataSourceIOException.Http(401, IOException("unauthenticated")))),
        )
        assertEquals(
            PlayerFailure.PERMISSION,
            classifyPlaybackFailure(exception(cause = MediaDataSourceIOException.Http(403, IOException("forbidden")))),
        )
    }

    private fun exception(
        errorCode: Int = PlaybackException.ERROR_CODE_UNSPECIFIED,
        cause: Throwable? = null,
    ) = PlaybackException("playback failed", cause, errorCode)
}
