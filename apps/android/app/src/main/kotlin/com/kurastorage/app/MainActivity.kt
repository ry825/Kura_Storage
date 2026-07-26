@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod")

package com.kurastorage.app

import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.ui.AppDestination
import com.kurastorage.core.ui.KuraStorageTheme
import com.kurastorage.feature.auth.AuthScreen
import com.kurastorage.feature.auth.AuthViewModel
import com.kurastorage.feature.connection.ConnectionScreen
import com.kurastorage.feature.connection.ConnectionViewModel

class MainActivity : ComponentActivity() {
    private lateinit var container: ServiceContainer
    private val connectionViewModel: ConnectionViewModel by viewModels {
        simpleViewModelFactory { ConnectionViewModel(container.connectionDetector) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        container = ServiceContainer(this)
        setContent {
            KuraStorageTheme {
                KuraStorageApp(container, connectionViewModel)
            }
        }
    }
}

@Composable
private fun KuraStorageApp(
    container: ServiceContainer,
    connectionViewModel: ConnectionViewModel,
) {
    val navController = rememberNavController()
    val connectionState by connectionViewModel.state.collectAsStateWithLifecycle()
    var connected by remember { mutableStateOf<ConnectionStatus.Connected?>(null) }
    var repository by remember { mutableStateOf<AuthenticationRepository?>(null) }

    DisposableEffect(Unit) {
        val observer =
            LifecycleEventObserver { _, event ->
                if (event == Lifecycle.Event.ON_RESUME) connectionViewModel.check()
            }
        (navController.context as? ComponentActivity)?.lifecycle?.addObserver(observer)
        onDispose {
            (navController.context as? ComponentActivity)?.lifecycle?.removeObserver(observer)
        }
    }

    NavHost(
        navController = navController,
        startDestination = AppDestination.CONNECTION.route,
    ) {
        composable(AppDestination.CONNECTION.route) {
            ConnectionScreen(
                state = connectionState,
                onRecheck = connectionViewModel::check,
                onConnected = { state ->
                    if (connected?.route != state.route || repository == null) {
                        connected = state
                        repository = container.authenticationRepository(state.route)
                    }
                    navController.navigate(AppDestination.AUTHENTICATION.route) {
                        launchSingleTop = true
                    }
                },
            )
        }
        composable(AppDestination.AUTHENTICATION.route) {
            val route = connected?.route
            val authRepository = repository
            if (route == null || authRepository == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val authViewModel: AuthViewModel =
                viewModel(
                    key = route.name,
                    factory =
                        simpleViewModelFactory {
                            AuthViewModel(route, Build.MODEL, authRepository)
                        },
                )
            val authState by authViewModel.state.collectAsStateWithLifecycle()
            AuthScreen(
                state = authState,
                onSubmit = authViewModel::submit,
                onRetry = {
                    navController.navigate(AppDestination.CONNECTION.route) {
                        popUpTo(AppDestination.CONNECTION.route) { inclusive = true }
                    }
                },
                onAuthenticated = {
                    navController.navigate(AppDestination.HOME.route) {
                        popUpTo(AppDestination.AUTHENTICATION.route) { inclusive = true }
                    }
                },
            )
        }
        composable(AppDestination.HOME.route) {
            val authRepository = repository
            val route = connected?.route
            if (authRepository == null || route == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val logoutViewModel: AuthViewModel =
                viewModel(
                    key = "logout-$route",
                    factory =
                        simpleViewModelFactory {
                            AuthViewModel(route, Build.MODEL, authRepository)
                        },
                )
            HomeScreen(
                connection = connected,
                onFiles = {},
                onTrash = {},
                onLogout = {
                    logoutViewModel.logout {
                        navController.navigate(AppDestination.CONNECTION.route) {
                            popUpTo(0)
                        }
                    }
                },
            )
        }
    }
}

@Composable
fun HomeScreen(
    connection: ConnectionStatus.Connected?,
    onFiles: () -> Unit,
    onTrash: () -> Unit,
    onLogout: () -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
    ) {
        Text("KuraStorage", style = MaterialTheme.typography.headlineMedium)
        Text("Connection: ${connection?.route?.name ?: "UNKNOWN"}")
        Button(onClick = onFiles) { Text("My files") }
        Button(onClick = onTrash) { Text("Trash") }
        Button(onClick = onLogout) { Text("Log out") }
    }
}
