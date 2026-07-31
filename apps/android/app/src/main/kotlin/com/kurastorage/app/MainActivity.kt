@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod")

package com.kurastorage.app

import android.os.Build
import android.os.Bundle
import android.provider.OpenableColumns
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
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
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.ui.AppDestination
import com.kurastorage.core.ui.KuraStorageTheme
import com.kurastorage.feature.auth.AuthScreen
import com.kurastorage.feature.auth.AuthViewModel
import com.kurastorage.feature.connection.ConnectionScreen
import com.kurastorage.feature.connection.ConnectionViewModel
import com.kurastorage.feature.files.FileBrowserScreen
import com.kurastorage.feature.files.FileBrowserViewModel

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
    var services by remember { mutableStateOf<SessionServices?>(null) }

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
                    if (connected?.route != state.route || services == null) {
                        connected = state
                        services = container.sessionServices(state.route)
                    }
                    navController.navigate(AppDestination.AUTHENTICATION.route) {
                        launchSingleTop = true
                    }
                },
            )
        }
        composable(AppDestination.AUTHENTICATION.route) {
            val route = connected?.route
            val authRepository = services?.authentication
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
            val authRepository = services?.authentication
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
                onFiles = { navController.navigate(AppDestination.FILES.route) },
                onTrash = { navController.navigate(AppDestination.TRASH.route) },
                onLogout = {
                    logoutViewModel.logout {
                        navController.navigate(AppDestination.CONNECTION.route) {
                            popUpTo(0)
                        }
                    }
                },
            )
        }
        composable(AppDestination.FILES.route) {
            val current = services
            if (current == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val filesViewModel: FileBrowserViewModel =
                viewModel(
                    key = "files",
                    factory =
                        simpleViewModelFactory {
                            FileBrowserViewModel(current.files, current.transfers)
                        },
                )
            FileRoute(
                viewModel = filesViewModel,
                trashMode = false,
                onExit = { navController.popBackStack() },
            )
        }
        composable(AppDestination.TRASH.route) {
            val current = services
            if (current == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val trashViewModel: FileBrowserViewModel =
                viewModel(
                    key = "trash",
                    factory =
                        simpleViewModelFactory {
                            FileBrowserViewModel(current.files, current.transfers, trashMode = true)
                        },
                )
            FileRoute(
                viewModel = trashViewModel,
                trashMode = true,
                onExit = { navController.popBackStack() },
            )
        }
    }
}

@Composable
private fun FileRoute(
    viewModel: FileBrowserViewModel,
    trashMode: Boolean,
    onExit: () -> Unit,
) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    var pendingDownload by remember { mutableStateOf<FileEntry?>(null) }
    val uploadPicker =
        rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            if (uri != null) {
                val metadata =
                    context.contentResolver
                        .query(
                            uri,
                            arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE),
                            null,
                            null,
                            null,
                        )?.use { cursor ->
                            if (!cursor.moveToFirst()) {
                                null
                            } else {
                                cursor.getString(0) to cursor.getLong(1)
                            }
                        }
                metadata?.let { (name, size) ->
                    viewModel.startUpload(uri.toString(), name, size, context.contentResolver.getType(uri))
                }
            }
        }
    val downloadPicker =
        rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("*/*")) { uri ->
            val file = pendingDownload
            if (uri != null && file != null) viewModel.startDownload(file, uri.toString())
            pendingDownload = null
        }
    FileBrowserScreen(
        state = state,
        trashMode = trashMode,
        onOpen = viewModel::open,
        onBack = { if (!viewModel.back()) onExit() },
        onRefresh = viewModel::refresh,
        onLoadMore = viewModel::loadMore,
        onCreateFolder = viewModel::createFolder,
        onChooseUpload = { uploadPicker.launch(arrayOf("*/*")) },
        onChooseDownload = { file ->
            pendingDownload = file
            downloadPicker.launch(file.name)
        },
        onTrash = viewModel::trash,
        onRestore = viewModel::restore,
        onDismissDetail = viewModel::dismissDetail,
        onCancelTransfer = viewModel::cancelTransfer,
        onRetryTransfer = viewModel::retryTransfer,
        onOpenDownload = { uri ->
            runCatching { context.startActivity(viewModel.downloadedFileIntent(uri)) }
        },
    )
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
