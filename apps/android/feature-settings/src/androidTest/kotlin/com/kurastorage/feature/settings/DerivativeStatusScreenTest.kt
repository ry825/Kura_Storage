package com.kurastorage.feature.settings

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import com.kurastorage.core.model.media.MediaDerivativeStatus
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

class DerivativeStatusScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun statusShowsCoverageCapacityFailuresAndAccessibleCounts() {
        compose.setContent {
            DerivativeStatusScreen(
                state =
                    DerivativeStatusState(
                        status =
                            MediaDerivativeStatus(
                                profileVersion = 2,
                                photoCount = 10,
                                readyCount = 7,
                                pendingCount = 1,
                                runningCount = 1,
                                failedCount = 1,
                                blockedCount = 0,
                                missingCount = 0,
                                readyBytes = 12_345,
                                duplicateCount = 0,
                                orphanCount = 2,
                                observedAt = java.time.Instant.parse("2026-09-07T00:00:00Z"),
                            ),
                    ),
                onRefresh = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Fast-display photo coverage").assertIsDisplayed()
        compose.onNodeWithText("7 / 10").assertIsDisplayed()
        compose.onNodeWithContentDescription("Failed: 1").assertIsDisplayed()
        compose.onNodeWithContentDescription("Old/orphaned: 2").assertIsDisplayed()
    }

    @Test
    fun errorProvidesRetryWithoutShowingZeroCapacity() {
        var retried = false
        compose.setContent {
            DerivativeStatusScreen(
                state = DerivativeStatusState(error = "offline"),
                onRefresh = { retried = true },
                onBack = {},
            )
        }

        compose.onNodeWithText("Status unavailable").assertIsDisplayed()
        compose.onNodeWithText("Retry").performClick()
        compose.runOnIdle { assertTrue(retried) }
    }
}
