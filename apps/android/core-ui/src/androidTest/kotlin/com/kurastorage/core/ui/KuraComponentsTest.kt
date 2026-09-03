package com.kurastorage.core.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertContentDescriptionEquals
import androidx.compose.ui.test.assertHeightIsAtLeast
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.assertWidthIsAtLeast
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.getBoundsInRoot
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraCardVariant
import com.kurastorage.core.ui.components.KuraIconButton
import com.kurastorage.core.ui.components.KuraPrimaryButton
import com.kurastorage.core.ui.components.KuraSectionHeader
import com.kurastorage.core.ui.components.KuraSegmentedControl
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.icons.KuraFileType
import com.kurastorage.core.ui.icons.KuraFileTypeIcon
import com.kurastorage.core.ui.icons.KuraLogo
import com.kurastorage.core.ui.state.KuraStateKind
import com.kurastorage.core.ui.state.KuraStateView
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class KuraComponentsTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun lightAndDarkThemesProduceDeterministicDifferentSurfaces() {
        var darkTheme by mutableStateOf(false)
        compose.setContent {
            KuraStorageTheme(darkTheme = darkTheme) {
                Box(Modifier.size(40.dp).background(androidx.compose.material3.MaterialTheme.colorScheme.background).testTag("surface"))
            }
        }
        val light = compose.onNodeWithTag("surface").captureToImage().toPixelMap()[1, 1]
        compose.runOnIdle { darkTheme = true }
        val dark = compose.onNodeWithTag("surface").captureToImage().toPixelMap()[1, 1]

        assertNotEquals(light, dark)
    }

    @Test
    fun cardsStatusesAndButtonsExposeVisualStates() {
        compose.setContent {
            KuraStorageTheme {
                androidx.compose.foundation.layout.Column {
                    KuraCard(modifier = Modifier.testTag("selected-card"), variant = KuraCardVariant.SELECTED) {
                        androidx.compose.material3.Text("Selected card")
                    }
                    KuraCard(variant = KuraCardVariant.DEFAULT) { androidx.compose.material3.Text("Default card") }
                    KuraCard(variant = KuraCardVariant.WARNING) { androidx.compose.material3.Text("Warning card") }
                    KuraCard(modifier = Modifier.testTag("disabled-card"), variant = KuraCardVariant.DISABLED) {
                        androidx.compose.material3.Text("Disabled card")
                    }
                    KuraStatusBadge("Neutral", KuraStatus.NEUTRAL)
                    KuraStatusBadge("Ready", KuraStatus.SUCCESS)
                    KuraStatusBadge("Attention", KuraStatus.WARNING)
                    KuraStatusBadge("Failed", KuraStatus.ERROR)
                    KuraStatusBadge("Information", KuraStatus.INFO)
                    KuraPrimaryButton("Enabled action", {})
                    KuraPrimaryButton("Disabled action", {}, enabled = false)
                }
            }
        }

        compose.onNodeWithText("Selected card").assertIsDisplayed()
        compose.onNodeWithTag("selected-card").assertIsSelected()
        compose.onNodeWithText("Default card").assertIsDisplayed()
        compose.onNodeWithText("Warning card").assertIsDisplayed()
        compose.onNodeWithText("Disabled card").assertIsDisplayed()
        compose.onNodeWithTag("disabled-card").assertIsNotEnabled()
        compose.onNodeWithText("Neutral").assertIsDisplayed()
        compose.onNodeWithText("Ready").assertIsDisplayed()
        compose.onNodeWithText("Attention").assertIsDisplayed()
        compose.onNodeWithText("Failed").assertIsDisplayed()
        compose.onNodeWithText("Information").assertIsDisplayed()
        compose.onNodeWithText("Enabled action").assertIsEnabled()
        compose.onNodeWithText("Disabled action").assertIsNotEnabled()
    }

    @Test
    fun stateViewsExposeErrorAndProgressSemantics() {
        compose.setContent {
            KuraStorageTheme {
                androidx.compose.foundation.layout.Column {
                    KuraStateView(
                        kind = KuraStateKind.LOADING,
                        title = "Loading items",
                        message = "Please wait",
                    )
                    KuraStateView(
                        kind = KuraStateKind.EMPTY,
                        title = "No items",
                        message = "Create the first item",
                    )
                    KuraStateView(
                        kind = KuraStateKind.RECOVERABLE_ERROR,
                        title = "Could not load",
                        message = "Connection interrupted",
                        modifier = Modifier.testTag("error-state"),
                        requestId = "request-1",
                        actionLabel = "Try again",
                        onAction = {},
                    )
                    KuraStateView(
                        kind = KuraStateKind.BLOCKING_ERROR,
                        title = "Access unavailable",
                        message = "Contact an administrator",
                    )
                    KuraStateView(
                        kind = KuraStateKind.PROGRESS,
                        title = "Uploading",
                        message = "2 of 4 files",
                        modifier = Modifier.testTag("progress-state"),
                        progress = 0.5f,
                    )
                }
            }
        }

        compose.onNodeWithText("Loading items").assertIsDisplayed()
        compose.onNodeWithText("No items").assertIsDisplayed()
        compose.onNodeWithText("Connection interrupted").assertIsDisplayed()
        compose.onNodeWithTag("error-state").assert(SemanticsMatcher.keyIsDefined(SemanticsProperties.Error))
        compose.onNodeWithText("Access unavailable").assertIsDisplayed()
        compose.onNodeWithText("Request ID: request-1").assertIsDisplayed()
        compose.onNodeWithTag("progress-state").assert(
            SemanticsMatcher.expectValue(
                SemanticsProperties.ProgressBarRangeInfo,
                ProgressBarRangeInfo(0.5f, 0f..1f),
            ),
        )
    }

    @Test
    fun headingsIconsSelectionAndTouchTargetsAreAccessible() {
        compose.setContent {
            KuraStorageTheme {
                androidx.compose.foundation.layout.Column {
                    KuraSectionHeader("Files")
                    KuraLogo(contentDescription = "KuraStorage logo")
                    KuraFileTypeIcon(KuraFileType.PDF)
                    KuraIconButton(contentDescription = "Open actions", onClick = {}) {
                        Box(Modifier.size(8.dp).background(Color.Black))
                    }
                    KuraSegmentedControl(listOf("First", "Second"), selectedIndex = 1, onSelected = {})
                }
            }
        }

        compose.onNodeWithText("Files").assert(SemanticsMatcher.keyIsDefined(SemanticsProperties.Heading))
        compose.onNodeWithContentDescription("KuraStorage logo").assertContentDescriptionEquals("KuraStorage logo")
        compose.onNodeWithContentDescription("PDF document").assertIsDisplayed()
        compose.onNodeWithContentDescription("Open actions").assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
        compose.onNodeWithText("Second").assertIsSelected()
    }

    @Test
    fun segmentedControlReflowsAtTwoHundredPercentFontScaleAndCaptures() {
        compose.setContent {
            val currentDensity = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(currentDensity.density, fontScale = 2f)) {
                KuraStorageTheme {
                    Box(Modifier.size(width = 360.dp, height = 300.dp).testTag("fixture")) {
                        KuraSegmentedControl(
                            labels = listOf("Low quality", "Medium quality", "Original quality"),
                            selectedIndex = 0,
                            onSelected = {},
                        )
                    }
                }
            }
        }

        val first = compose.onNodeWithText("Low quality").getBoundsInRoot()
        val second = compose.onNodeWithText("Medium quality").getBoundsInRoot()
        assertTrue(second.top >= first.bottom)
        compose.onNodeWithTag("fixture").captureToImage()
    }
}
