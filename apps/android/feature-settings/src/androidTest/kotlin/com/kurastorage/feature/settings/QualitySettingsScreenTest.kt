package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.media.QualityPreferences
import org.junit.Rule
import org.junit.Test

class QualitySettingsScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun screenExplainsDataUseAndShowsEveryConnectionContext() {
        compose.setContent {
            QualitySettingsScreen(
                state = QualitySettingsState(preferences = QualityPreferences(), loading = false),
                onSelect = { _, _ -> },
                onSave = {},
                onReset = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Media quality and data use").assertIsDisplayed()
        compose.onNodeWithText("Local direct connection").assertIsDisplayed()
        compose.onNodeWithText("Registered external Wi-Fi + ZeroTier").assertIsDisplayed()
        compose.onNodeWithText("Unregistered Wi-Fi + ZeroTier").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Mobile + ZeroTier").performScrollTo().assertIsDisplayed()
        compose
            .onNodeWithText(
                "Mobile data is never available for automatic backup.",
                substring = true,
            ).performScrollTo()
            .assertIsDisplayed()
        compose.onNodeWithText("Save").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Reset to defaults").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Back").performScrollTo().assertIsDisplayed()
    }

    @Test
    fun saveAndResetAreExplicitActions() {
        var saves = 0
        var resets = 0
        compose.setContent {
            QualitySettingsScreen(
                state = QualitySettingsState(preferences = QualityPreferences(), loading = false, dirty = true),
                onSelect = { _, _ -> },
                onSave = { saves++ },
                onReset = { resets++ },
                onBack = {},
            )
        }

        compose.onNodeWithText("Save").performScrollTo().performClick()
        compose.onNodeWithText("Reset to defaults").performScrollTo().performClick()
        compose.runOnIdle {
            org.junit.Assert.assertEquals(1, saves)
            org.junit.Assert.assertEquals(1, resets)
        }
    }

    @Test
    fun primaryActionsRemainReachableInLandscapeAtTwoHundredPercentText() {
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, 2f)) {
                MaterialTheme(colorScheme = darkColorScheme()) {
                    Box(Modifier.size(width = 800.dp, height = 360.dp)) {
                        QualitySettingsScreen(
                            state = QualitySettingsState(preferences = QualityPreferences(), loading = false, dirty = true),
                            onSelect = { _, _ -> },
                            onSave = {},
                            onReset = {},
                            onBack = {},
                        )
                    }
                }
            }
        }
        compose.onNodeWithText("Save").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Reset to defaults").performScrollTo().assertIsDisplayed()
        compose.onRoot().captureToImage()
    }
}
