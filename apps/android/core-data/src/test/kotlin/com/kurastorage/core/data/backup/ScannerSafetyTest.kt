package com.kurastorage.core.data.backup

import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class ScannerSafetyTest {
    @Test
    fun relativePathNormalizesSegmentsWithoutAllowingTraversalOrControls() {
        assertEquals("Camera/photo.jpg", ScannerPathPolicy.normalize(listOf(" Camera ", "photo.jpg")))
        assertThrows(IllegalArgumentException::class.java) { ScannerPathPolicy.normalize(listOf("..", "secret")) }
        assertThrows(IllegalArgumentException::class.java) { ScannerPathPolicy.normalize(listOf("bad\u0000name")) }
        assertThrows(IllegalArgumentException::class.java) { ScannerPathPolicy.normalize(listOf("/absolute")) }
    }

    @Test
    fun identityChangesOnlyWhenProviderReusesAnIdentifier() {
        val first = LocalDocumentIdentityResolver.resolve(null, "discriminator-a") { "opaque-a" }
        val stable = LocalDocumentIdentityResolver.resolve(first, "discriminator-a") { "opaque-b" }
        val reused = LocalDocumentIdentityResolver.resolve(first, "discriminator-b") { "opaque-b" }

        assertEquals("opaque-a", stable.localDocumentKey)
        assertEquals("opaque-b", reused.localDocumentKey)
    }

    @Test
    @OptIn(ExperimentalCoroutinesApi::class)
    fun burstSignalsDebounceToOneScanRequest() =
        runTest {
            var dispatchCount = 0
            val dispatcher = DebouncedScanDispatcher(this, 1_000) { dispatchCount++ }
            dispatcher.submit()
            dispatcher.submit()
            dispatcher.submit()
            advanceTimeBy(999)
            runCurrent()
            assertEquals(0, dispatchCount)
            advanceTimeBy(1)
            runCurrent()
            assertEquals(1, dispatchCount)
            dispatcher.close()
        }
}
