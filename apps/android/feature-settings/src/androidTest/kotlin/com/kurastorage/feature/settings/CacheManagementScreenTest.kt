package com.kurastorage.feature.settings

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.unit.Density
import com.kurastorage.core.model.media.AdminMediaCacheStatus
import com.kurastorage.core.model.media.MediaCleanupRun
import com.kurastorage.core.model.media.MediaCleanupRunStatus
import com.kurastorage.core.model.media.MediaCleanupTrigger
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class CacheManagementScreenTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun cacheUsageExcludesThumbnailAndCleanupRequiresConfirmation() {
        var requests = 0
        compose.setContent {
            CacheManagementScreen(
                state = CacheManagementState(loading = false, status = status()),
                onRefresh = {},
                onCleanup = { requests++ },
                onBack = {},
            )
        }
        compose.onNodeWithText("10 MB / 100 MB").assertIsDisplayed()
        compose.onAllNodesWithText("Thumbnail", substring = true).assertCountEquals(0)
        compose.onNodeWithTag("cache-management").performScrollToNode(hasText("Clean up now"))
        compose.onNodeWithText("Clean up now").performClick()
        compose.onNodeWithText("Original files, thumbnails, generating items", substring = true).assertIsDisplayed()
        assertEquals(0, requests)
        compose.onNodeWithText("Request cleanup").performClick()
        assertEquals(1, requests)
        compose.onRoot().captureToImage()
    }

    @Test
    fun forbiddenRouteIsSafeAndLargeDarkContentRemainsReachable() {
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, 2f)) {
                MaterialTheme(colorScheme = darkColorScheme()) {
                    CacheManagementScreen(
                        CacheManagementState(
                            loading = false,
                            access = CacheAccessState.FORBIDDEN,
                            error = "Cache management is available to administrators only. No management action was performed.",
                        ),
                        {},
                        {},
                        {},
                    )
                }
            }
        }
        compose.onNodeWithText("Administrator access required").assertIsDisplayed()
        compose.onNodeWithText("No management action was performed.", substring = true).assertIsDisplayed()
        compose.onAllNodesWithText("Clean up now").assertCountEquals(0)
        val title = compose.onNodeWithTag("cache-title").captureToImage().toPixelMap()
        val brightestPixel =
            (0 until title.height).maxOf { y ->
                (0 until title.width).maxOf { x ->
                    val color = title[x, y]
                    (color.red + color.green + color.blue) / 3f
                }
            }
        assertTrue("Dark theme title must remain visibly brighter than its background", brightestPixel > 0.45f)
    }

    @Test
    fun runningCleanupStateRemainsServerAuthoritative() {
        compose.setContent {
            CacheManagementScreen(
                CacheManagementState(
                    loading = false,
                    status = status().copy(lastCleanupRun = run(MediaCleanupRunStatus.RUNNING), runningRunCount = 1),
                    requestingCleanup = true,
                ),
                {},
                {},
                {},
            )
        }
        compose.onNodeWithText("Latest cleanup: Running").assertIsDisplayed()
        compose.onNodeWithContentDescription("Cleanup queue: 0 pending, 1 running").assertIsDisplayed()
    }

    @Test
    fun failedCleanupShowsFailureWithoutRetryingIndividualFiles() {
        compose.setContent {
            CacheManagementScreen(
                CacheManagementState(
                    loading = false,
                    status = status().copy(lastCleanupRun = run(MediaCleanupRunStatus.FAILED).copy(failureCount = 2)),
                ),
                {},
                {},
                {},
            )
        }
        compose.onNodeWithText("Latest cleanup: Failed").assertIsDisplayed()
        compose.onNodeWithText("Deletion failures: 2").assertIsDisplayed()
        compose.onAllNodesWithText("Retry", substring = true).assertCountEquals(0)
    }

    private fun status() =
        AdminMediaCacheStatus(
            10L * 1024 * 1024,
            1L * 1024 * 1024,
            2L * 1024 * 1024,
            3L * 1024 * 1024,
            4L * 1024 * 1024,
            100L * 1024 * 1024,
            60L * 1024 * 1024,
            1,
            1,
            2,
            0,
            0,
            null,
        )

    private fun run(status: MediaCleanupRunStatus) =
        MediaCleanupRun(
            "22222222-2222-2222-2222-222222222222",
            MediaCleanupTrigger.MANUAL,
            status,
            Instant.EPOCH,
            Instant.EPOCH,
            null,
            2,
            1,
            1024,
            0,
            null,
            null,
        )
}
