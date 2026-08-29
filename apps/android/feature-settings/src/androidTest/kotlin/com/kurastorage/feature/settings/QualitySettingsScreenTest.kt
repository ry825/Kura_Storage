package com.kurastorage.feature.settings

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performScrollTo
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
                onBack = {},
            )
        }

        compose.onNodeWithText("Media quality and data use").assertIsDisplayed()
        compose.onNodeWithText("Local direct connection").assertIsDisplayed()
        compose.onNodeWithText("Registered remote Wi-Fi").assertIsDisplayed()
        compose.onNodeWithText("Other remote Wi-Fi").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Mobile connection").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Back").performScrollTo().assertIsDisplayed()
    }
}
