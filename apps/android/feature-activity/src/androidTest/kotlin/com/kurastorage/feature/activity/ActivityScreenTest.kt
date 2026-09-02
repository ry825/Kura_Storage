package com.kurastorage.feature.activity

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performScrollToIndex
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ActivityDeleteKind
import com.kurastorage.core.model.ActivityDetail
import com.kurastorage.core.model.ActivityItem
import com.kurastorage.core.model.ActivityTargetType
import com.kurastorage.core.model.UserActivityType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class ActivityScreenTest {
    @get:Rule val compose = createComposeRule()

    @Test fun rendersTypedActivityPagingFiltersAndAccessibleOpenLink() {
        var selected: UserActivityType? = null
        var opened = false
        var loadedMore = false
        compose.setContent {
            ActivityScreen(
                ActivityUiState(items = listOf(item()), canLoadMore = true),
                {},
                {},
                { selected = it },
                { loadedMore = true },
                { opened = true },
            )
        }

        compose.onNode(hasContentDescription("Delete operation")).assertIsDisplayed()
        compose.onNodeWithText("Permanently deleted").assertIsDisplayed()
        compose.onNodeWithText("Snapshot only — current item is unavailable").assertIsDisplayed().performClick()
        compose.onNodeWithText("Edit").performClick()
        compose.onNodeWithText("Load more").performScrollTo().performClick()
        compose.runOnIdle {
            assertFalse(opened)
            assertEquals(UserActivityType.EDIT, selected)
            assertTrue(loadedMore)
        }
    }

    @Test fun loadingEmptyErrorUnknownAndLargeFontRemainSafe() {
        val state = mutableStateOf(ActivityUiState(loading = true))
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                Box(Modifier.requiredSize(280.dp, 360.dp)) {
                    ActivityScreen(state.value, {}, {}, {}, {}, {})
                }
            }
        }
        compose.onNodeWithText("Loading activity").performScrollTo().assertIsDisplayed()
        compose.runOnIdle { state.value = ActivityUiState() }
        compose.onNodeWithText("No activity to show.").performScrollTo().assertIsDisplayed()
        compose.runOnIdle {
            state.value =
                ActivityUiState(error = ActivityUiError("Offline", com.kurastorage.core.model.ErrorCategory.CONNECTION))
        }
        compose.onNodeWithText("Offline").performScrollTo().assertIsDisplayed()
        compose.runOnIdle {
            state.value = ActivityUiState(items = listOf(item().copy(type = UserActivityType.UNKNOWN, detail = ActivityDetail.Unsupported)))
        }
        compose.onNodeWithTag("activity-list").performScrollToIndex(1)
        compose.onNodeWithText("This activity requires a newer app version.").performScrollTo().assertIsDisplayed()
        val screenshot = compose.onRoot().captureToImage()
        assertTrue(screenshot.width > 0 && screenshot.height > 0)
    }

    private companion object {
        fun item() =
            ActivityItem(
                UserActivityType.DELETE,
                Instant.parse("2026-09-02T01:02:03Z"),
                "A very long actor display name that remains readable",
                null,
                null,
                ActivityTargetType.FILE,
                "a-very-long-file-name-that-remains-a-snapshot-after-purge.txt",
                "Former owner",
                ActivityDetail.Delete(ActivityDeleteKind.PURGED),
            )
    }
}
