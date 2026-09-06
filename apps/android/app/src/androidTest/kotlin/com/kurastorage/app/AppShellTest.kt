package com.kurastorage.app

import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.kurastorage.core.ui.AppDestination
import com.kurastorage.core.ui.KuraStorageTheme
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AppShellTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun authenticatedShellShowsFiveDestinationsAndDoesNotDuplicateReselection() {
        lateinit var controller: NavHostController
        compose.setContent {
            KuraStorageTheme {
                controller = rememberNavController()
                KuraStorageAppShell(controller) { padding ->
                    NavHost(
                        navController = controller,
                        startDestination = AppDestination.HOME.route,
                        modifier = Modifier.padding(padding),
                    ) {
                        TopLevelDestination.entries.forEach { item ->
                            composable(item.destination.route) { Text("${item.label} body") }
                        }
                    }
                }
            }
        }

        compose.onNodeWithContentDescription("Primary navigation").assertIsDisplayed()
        TopLevelDestination.entries.forEach {
            compose.onNodeWithTag("top-level-${it.destination.route}").assertIsDisplayed()
        }
        compose.onNodeWithTag("top-level-home").assertIsSelected()

        compose.onNodeWithTag("top-level-files").performClick()
        compose.onNodeWithText("Files body").assertIsDisplayed()
        compose.onNodeWithTag("top-level-files").assertIsSelected().performClick()
        compose.runOnIdle { check(controller.popBackStack()) }
        compose.onNodeWithText("Home body").assertIsDisplayed()

        listOf("sharing", "search", "settings").forEach { route ->
            compose.onNodeWithTag("top-level-$route").performClick().assertIsSelected()
        }
    }

    @Test
    fun protectedAndImmersiveRoutesDoNotShowBottomNavigation() {
        compose.setContent {
            KuraStorageTheme {
                val controller = rememberNavController()
                KuraStorageAppShell(controller) { padding ->
                    NavHost(
                        navController = controller,
                        startDestination = AppDestination.AUTHENTICATION.route,
                        modifier = Modifier.padding(padding),
                    ) {
                        composable(AppDestination.AUTHENTICATION.route) { Text("Authentication body") }
                    }
                }
            }
        }

        compose.onNodeWithText("Authentication body").assertIsDisplayed()
        compose.onNodeWithContentDescription("Primary navigation").assertDoesNotExist()
    }

    @Test
    fun searchAndSearchViewerReturnToSingleHomeDestination() {
        lateinit var controller: NavHostController
        compose.setContent {
            KuraStorageTheme {
                controller = rememberNavController()
                KuraStorageAppShell(controller) { padding ->
                    NavHost(
                        navController = controller,
                        startDestination = AppDestination.HOME.route,
                        modifier = Modifier.padding(padding),
                    ) {
                        composable(AppDestination.HOME.route) { Text("Home body") }
                        composable(AppDestination.SEARCH.route) { Text("Search body") }
                        composable(AppDestination.PHOTO_VIEWER.route) { Text("Viewer body") }
                    }
                }
            }
        }

        compose.onNodeWithTag("top-level-search").performClick()
        compose.onNodeWithText("Search body").assertIsDisplayed()
        compose.onNodeWithTag("top-level-home").performClick()
        compose.onNodeWithText("Home body").assertIsDisplayed()

        compose.onNodeWithTag("top-level-search").performClick()
        compose.runOnIdle { controller.navigate(AppDestination.PHOTO_VIEWER.route) }
        compose.onNodeWithText("Viewer body").assertIsDisplayed()
        compose.runOnIdle { check(controller.popBackStack()) }
        compose.onNodeWithTag("top-level-home").performClick()
        compose.onNodeWithText("Home body").assertIsDisplayed()
        compose.onNodeWithTag("top-level-home").performClick().assertIsSelected()
        compose.runOnIdle { check(!controller.popBackStack()) }
    }

    @Test
    fun logoutNavigationClearsProtectedBackStack() {
        lateinit var controller: NavHostController
        compose.setContent {
            controller = rememberNavController()
            NavHost(
                navController = controller,
                startDestination = AppDestination.HOME.route,
            ) {
                composable(AppDestination.CONNECTION.route) { Text("Connection body") }
                composable(AppDestination.HOME.route) { Text("Home body") }
                composable(AppDestination.SETTINGS.route) { Text("Settings body") }
            }
        }
        compose.runOnIdle { controller.navigate(AppDestination.SETTINGS.route) }
        compose.onNodeWithText("Settings body").assertIsDisplayed()

        compose.runOnIdle { controller.navigateToConnection() }

        compose.onNodeWithText("Connection body").assertIsDisplayed()
        compose.runOnIdle { check(!controller.popBackStack()) }
    }
}
