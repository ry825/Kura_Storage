@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "MatchingDeclarationName")

package com.kurastorage.app

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.navigation.NavHostController
import androidx.navigation.compose.currentBackStackEntryAsState
import com.kurastorage.core.ui.AppDestination
import com.kurastorage.core.ui.components.KuraAppScaffold
import com.kurastorage.core.ui.components.KuraTopAppBar

enum class TopLevelDestination(
    val destination: AppDestination,
    val label: String,
    val symbol: String,
) {
    HOME(AppDestination.HOME, "Home", "⌂"),
    FILES(AppDestination.FILES, "Files", "▤"),
    SHARING(AppDestination.SHARING, "Shared", "⇄"),
    SEARCH(AppDestination.SEARCH, "Search", "⌕"),
    SETTINGS(AppDestination.SETTINGS, "Settings", "⚙"),
}

fun topLevelDestinationFor(route: String?): TopLevelDestination? {
    val baseRoute = route?.substringBefore('?') ?: return null
    return TopLevelDestination.entries.firstOrNull { it.destination.route == baseRoute }
}

fun shouldReplaceSession(
    previousSessionId: String?,
    nextSessionId: String?,
): Boolean = previousSessionId != null && previousSessionId != nextSessionId

fun NavHostController.navigateToTopLevel(destination: TopLevelDestination) {
    navigate(destination.destination.route) {
        popUpTo(AppDestination.HOME.route) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }
}

fun NavHostController.navigateToConnection() {
    navigate(AppDestination.CONNECTION.route) {
        popUpTo(0)
        launchSingleTop = true
    }
}

@Composable
fun KuraStorageAppShell(
    navController: NavHostController,
    modifier: Modifier = Modifier,
    floatingActionButton: @Composable () -> Unit = {},
    content: @Composable (PaddingValues) -> Unit,
) {
    val backStackEntry by navController.currentBackStackEntryAsState()
    val selected = topLevelDestinationFor(backStackEntry?.destination?.route)
    val snackbarHostState = remember { SnackbarHostState() }

    if (selected == null) {
        content(PaddingValues())
        return
    }

    KuraAppScaffold(
        modifier = modifier,
        topBar = { KuraTopAppBar(selected.label) },
        bottomBar = {
            KuraBottomNavigation(
                selected = selected,
                onSelected = navController::navigateToTopLevel,
            )
        },
        floatingActionButton = floatingActionButton,
        snackbarHost = { SnackbarHost(snackbarHostState) },
        content = content,
    )
}

@Composable
private fun KuraBottomNavigation(
    selected: TopLevelDestination,
    onSelected: (TopLevelDestination) -> Unit,
) {
    NavigationBar(modifier = Modifier.semantics { contentDescription = "Primary navigation" }) {
        TopLevelDestination.entries.forEach { item ->
            NavigationBarItem(
                modifier = Modifier.testTag("top-level-${item.destination.route}"),
                selected = item == selected,
                onClick = { onSelected(item) },
                icon = { Text(item.symbol) },
                label = { Text(item.label) },
                alwaysShowLabel = true,
            )
        }
    }
}
