package com.kurastorage.app

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import org.junit.Rule
import org.junit.Test

class HomeScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun homeContainsOnlyMvpEntriesAndConnection() {
        compose.setContent {
            HomeScreen(
                connection =
                    ConnectionStatus.Connected(
                        ConnectionRoute.REMOTE_SECURE,
                        StorageAvailability.AVAILABLE,
                    ),
                onFiles = {},
                onTrash = {},
                onLogout = {},
            )
        }

        compose.onNodeWithText("My files").assertIsDisplayed()
        compose.onNodeWithText("Trash").assertIsDisplayed()
        compose.onNodeWithText("Connection: REMOTE_SECURE").assertIsDisplayed()
        compose.onNodeWithText("Log out").assertIsDisplayed()
    }
}
