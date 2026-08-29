package com.kurastorage.app

import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performScrollTo
import com.kurastorage.core.model.AdminStorageStatus
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.feature.files.AdminStorageState
import org.junit.Rule
import org.junit.Test

class HomeScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun homeContainsNavigationEntriesAndConnection() {
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
        compose.onNodeWithText("Trash").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Search").assertIsDisplayed()
        compose.onNodeWithText("Recent files").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Favorites").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Tags").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Connection: REMOTE_SECURE").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Log out").performScrollTo().assertIsDisplayed()
    }

    @Test
    fun homeShowsCapacityWarningOnlyWhenAdminStatusIsPresent() {
        val warning = AdminStorageStatus("AVAILABLE", 100, 10, 20, true, 5, 1, 30, 0, null)
        compose.setContent {
            HomeScreen(
                connection = null,
                adminStorageState = AdminStorageState(loading = false, status = warning),
                onFiles = {},
                onTrash = {},
                onLogout = {},
            )
        }
        compose.onNodeWithText("Storage capacity warning").assertIsDisplayed()
    }

    @Test
    fun homeHidesCapacityDetailsForMemberState() {
        compose.setContent {
            HomeScreen(
                connection = null,
                adminStorageState = AdminStorageState(loading = false),
                onFiles = {},
                onTrash = {},
                onLogout = {},
            )
        }
        compose.onAllNodesWithText("Storage capacity warning").assertCountEquals(0)
    }
}
