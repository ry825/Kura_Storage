@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
)

package com.kurastorage.app

import android.net.Uri
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
import androidx.compose.runtime.LaunchedEffect
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
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.kurastorage.core.data.SharingRepository
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.ShareScope
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.ui.AppDestination
import com.kurastorage.core.ui.KuraStorageTheme
import com.kurastorage.feature.auth.AuthScreen
import com.kurastorage.feature.auth.AuthViewModel
import com.kurastorage.feature.connection.ConnectionScreen
import com.kurastorage.feature.connection.ConnectionViewModel
import com.kurastorage.feature.files.AdminStoragePanel
import com.kurastorage.feature.files.AdminStorageState
import com.kurastorage.feature.files.AdminStorageViewModel
import com.kurastorage.feature.files.FileBrowserScreen
import com.kurastorage.feature.files.FileBrowserViewModel
import com.kurastorage.feature.search.RecentFilesScreen
import com.kurastorage.feature.search.RecentFilesViewModel
import com.kurastorage.feature.search.SearchFilterOption
import com.kurastorage.feature.search.SearchScreen
import com.kurastorage.feature.search.SearchViewModel
import com.kurastorage.feature.sharing.SharingListViewModel
import com.kurastorage.feature.sharing.SharingScreen
import com.kurastorage.feature.sharing.SharingSettingsScreen
import com.kurastorage.feature.sharing.SharingSettingsViewModel

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
            val current = services
            val authRepository = current?.authentication
            val route = connected?.route
            if (current == null || authRepository == null || route == null) {
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
            val storageViewModel =
                if (authRepository.role() == UserRole.ADMIN) {
                    viewModel<AdminStorageViewModel>(
                        key = "home-storage-$route",
                        factory = simpleViewModelFactory { AdminStorageViewModel(current.adminStorage) },
                    )
                } else {
                    null
                }
            val storageState = AdminStorageStateFor(storageViewModel)
            HomeScreen(
                connection = connected,
                adminStorageState = storageState,
                onRefreshAdminStorage = { storageViewModel?.refresh() },
                onFiles = { navController.navigate(AppDestination.FILES.route) },
                onShared = { navController.navigate(AppDestination.SHARING.route) },
                onSearch = { navController.navigate(AppDestination.SEARCH.route) },
                onRecent = { navController.navigate(AppDestination.RECENT_FILES.route) },
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
                            FileBrowserViewModel(current.files, current.transfers, recentFiles = current.recentFiles)
                        },
                )
            val storageViewModel =
                if (current.authentication.role() == UserRole.ADMIN) {
                    viewModel<AdminStorageViewModel>(
                        key = "files-storage",
                        factory = simpleViewModelFactory { AdminStorageViewModel(current.adminStorage) },
                    )
                } else {
                    null
                }
            FileRoute(
                viewModel = filesViewModel,
                adminStorageViewModel = storageViewModel,
                trashMode = false,
                onOpenTrash = { navController.navigate(AppDestination.TRASH.route) },
                onExit = { navController.popBackStack() },
                onShare = { entry -> navController.navigate(settingsRoute("new", entry.id, entry.entryType, entry.name)) },
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
                adminStorageViewModel = null,
                trashMode = true,
                onOpenTrash = {},
                onExit = { navController.popBackStack() },
                onShare = {},
            )
        }
        composable(AppDestination.SHARING.route) {
            val current = services
            if (current == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val sharingViewModel: SharingListViewModel =
                viewModel(
                    key = "sharing-${connected?.route}",
                    factory = simpleViewModelFactory { SharingListViewModel(current.sharing) },
                )
            val state by sharingViewModel.state.collectAsStateWithLifecycle()
            SharingScreen(
                state,
                onBack = { navController.popBackStack() },
                onScope = sharingViewModel::selectScope,
                onType = sharingViewModel::selectTargetType,
                onRefresh = sharingViewModel::refresh,
                onLoadMore = sharingViewModel::loadMore,
                onOpenTarget = { share ->
                    navController.navigate("shared-entry/${share.targetEntryId}/${share.entryType.name}")
                },
                onManage = { share ->
                    navController.navigate(settingsRoute(share.id, share.targetEntryId, share.entryType, share.name))
                },
            )
        }
        composable(AppDestination.SEARCH.route) {
            val current = services
            if (current == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val searchViewModel: SearchViewModel =
                viewModel(
                    key = "search-${connected?.route}",
                    factory = simpleViewModelFactory { SearchViewModel(current.search, current.files::detail) },
                )
            val state by searchViewModel.state.collectAsStateWithLifecycle()
            var ownerOptions by remember { mutableStateOf(emptyList<SearchFilterOption>()) }
            var shareOptions by remember { mutableStateOf(emptyList<SearchFilterOption>()) }
            var filterOptionsGeneration by remember { mutableStateOf(0) }
            LaunchedEffect(current, filterOptionsGeneration) {
                runCatching {
                    val personalOwners =
                        current.files
                            .list(null, page = 1, pageSize = 1)
                            .items
                            .map { it.owner }
                    val received = loadAllReceivedShares(current.sharing)
                    ownerOptions =
                        (personalOwners + received.map { it.owner })
                            .filter { it.id.isNotBlank() }
                            .distinctBy { it.id }
                            .map { SearchFilterOption(it.id, it.displayName) }
                    shareOptions =
                        received
                            .distinctBy { it.targetEntryId }
                            .map { SearchFilterOption(it.targetEntryId, it.name) }
                }
            }
            SearchScreen(
                state = state,
                onBack = { navController.popBackStack() },
                onInput = searchViewModel::updateInput,
                onSearch = searchViewModel::search,
                onRefresh = {
                    searchViewModel.refresh()
                    filterOptionsGeneration++
                },
                onLoadMore = searchViewModel::loadMore,
                onOpen = { item ->
                    searchViewModel.open(item) { id, type -> navController.navigate(entryRoute(id, type)) }
                },
                ownerOptions = ownerOptions,
                shareOptions = shareOptions,
            )
        }
        composable(AppDestination.RECENT_FILES.route) {
            val current = services
            if (current == null) {
                navController.navigate(AppDestination.CONNECTION.route)
                return@composable
            }
            val recentViewModel: RecentFilesViewModel =
                viewModel(
                    key = "recent-${connected?.route}",
                    factory = simpleViewModelFactory { RecentFilesViewModel(current.recentFiles, current.files::detail) },
                )
            val state by recentViewModel.state.collectAsStateWithLifecycle()
            var shareOptions by remember { mutableStateOf(emptyList<SearchFilterOption>()) }
            var shareOptionsGeneration by remember { mutableStateOf(0) }
            LaunchedEffect(current, shareOptionsGeneration) {
                runCatching {
                    shareOptions =
                        loadAllReceivedShares(current.sharing)
                            .distinctBy { it.targetEntryId }
                            .map { SearchFilterOption(it.targetEntryId, it.name) }
                }
            }
            RecentFilesScreen(
                state = state,
                onBack = { navController.popBackStack() },
                onRefresh = {
                    recentViewModel.refresh()
                    shareOptionsGeneration++
                },
                onLoadMore = recentViewModel::loadMore,
                onOpen = { item ->
                    recentViewModel.open(item) { id, type -> navController.navigate(entryRoute(id, type)) }
                },
                shareOptions = shareOptions,
            )
        }
        composable(
            route = "shared-entry/{entryId}/{entryType}",
            arguments =
                listOf(
                    navArgument("entryId") { type = NavType.StringType },
                    navArgument("entryType") { type = NavType.StringType },
                ),
        ) { backStackEntry ->
            val current = services ?: return@composable
            val entryId = checkNotNull(backStackEntry.arguments?.getString("entryId"))
            val type = FileEntryType.valueOf(checkNotNull(backStackEntry.arguments?.getString("entryType")))
            val filesViewModel: FileBrowserViewModel =
                viewModel(
                    key = "shared-entry-$entryId-${connected?.route}",
                    factory =
                        simpleViewModelFactory {
                            FileBrowserViewModel(
                                current.files,
                                current.transfers,
                                initialParentId = entryId.takeIf { type == FileEntryType.FOLDER },
                                initialSelectionId = entryId.takeIf { type == FileEntryType.FILE },
                                recentFiles = current.recentFiles,
                            )
                        },
                )
            FileRoute(
                viewModel = filesViewModel,
                adminStorageViewModel = null,
                trashMode = false,
                onOpenTrash = {},
                onExit = { navController.popBackStack() },
                onShare = { entry -> navController.navigate(settingsRoute("new", entry.id, entry.entryType, entry.name)) },
            )
        }
        composable(
            route = "${AppDestination.SHARING_SETTINGS.route}/{shareId}/{targetEntryId}/{entryType}/{targetName}",
            arguments =
                listOf(
                    navArgument("shareId") { type = NavType.StringType },
                    navArgument("targetEntryId") { type = NavType.StringType },
                    navArgument("entryType") { type = NavType.StringType },
                    navArgument("targetName") { type = NavType.StringType },
                ),
        ) { backStackEntry ->
            val current = services ?: return@composable
            val arguments = checkNotNull(backStackEntry.arguments)
            val shareId = arguments.getString("shareId").takeUnless { it == "new" }
            val targetId = checkNotNull(arguments.getString("targetEntryId"))
            val targetType = FileEntryType.valueOf(checkNotNull(arguments.getString("entryType")))
            val targetName = Uri.decode(checkNotNull(arguments.getString("targetName")))
            val settingsViewModel: SharingSettingsViewModel =
                viewModel(
                    key = "sharing-settings-${shareId ?: targetId}-${connected?.route}",
                    factory =
                        simpleViewModelFactory {
                            SharingSettingsViewModel(current.sharing, targetId, targetType, targetName, shareId)
                        },
                )
            val state by settingsViewModel.state.collectAsStateWithLifecycle()
            SharingSettingsScreen(
                state,
                onBack = { navController.popBackStack() },
                onRefresh = settingsViewModel::refresh,
                onCandidate = settingsViewModel::selectCandidate,
                onPermission = settingsViewModel::selectPermission,
                onSubmitMember = settingsViewModel::submitSelectedMember,
                onChangePermission = settingsViewModel::changeMemberPermission,
                onRemoveMember = settingsViewModel::requestMemberRemoval,
                onDeleteShare = settingsViewModel::requestShareDeletion,
                onConfirm = settingsViewModel::confirm,
                onDismissConfirmation = settingsViewModel::dismissConfirmation,
            )
        }
    }
}

@Composable
private fun FileRoute(
    viewModel: FileBrowserViewModel,
    adminStorageViewModel: AdminStorageViewModel?,
    trashMode: Boolean,
    onOpenTrash: () -> Unit,
    onExit: () -> Unit,
    onShare: (FileEntry) -> Unit,
) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val adminStorageState = AdminStorageStateFor(adminStorageViewModel)
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
        onShowDetails = viewModel::select,
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
        onBeginPermanentDelete = viewModel::beginPermanentDelete,
        onConfirmPermanentDelete = viewModel::confirmPermanentDelete,
        onCancelPermanentDelete = viewModel::cancelPermanentDelete,
        onRecheckMissing = viewModel::recheckMissing,
        onBeginMissingIndexDelete = viewModel::beginMissingIndexDelete,
        onConfirmMissingIndexDelete = viewModel::confirmMissingIndexDelete,
        onCancelMissingIndexDelete = viewModel::cancelMissingIndexDelete,
        onRename = viewModel::beginRename,
        onRenameInput = viewModel::updateRenameInput,
        onSubmitRename = viewModel::submitRename,
        onDismissRename = viewModel::dismissRename,
        onMove = viewModel::beginMove,
        onOpenMoveFolder = viewModel::openMoveFolder,
        onBackMoveFolder = viewModel::backMoveFolder,
        onLoadMoreMoveFolders = viewModel::loadMoreMoveFolders,
        onConfirmMove = viewModel::confirmMove,
        onDismissMove = viewModel::dismissMove,
        onRefreshPlacement = viewModel::refreshAfterPlacementFailure,
        onDetailDisplayed = viewModel::detailDisplayed,
        onDismissDetail = viewModel::dismissDetail,
        onCancelTransfer = viewModel::cancelTransfer,
        onRetryTransfer = viewModel::retryTransfer,
        onOpenDownload = { uri ->
            runCatching { context.startActivity(viewModel.downloadedFileIntent(uri)) }
        },
        adminStorageState = adminStorageState,
        onRefreshAdminStorage = { adminStorageViewModel?.refresh() },
        onOpenTrashFromWarning = onOpenTrash,
        onShare = onShare,
    )
}

@Composable
private fun AdminStorageStateFor(viewModel: AdminStorageViewModel?): AdminStorageState {
    if (viewModel == null) return AdminStorageState(loading = false)
    val state by viewModel.state.collectAsStateWithLifecycle()
    return state
}

@Composable
@Suppress("LongParameterList")
fun HomeScreen(
    connection: ConnectionStatus.Connected?,
    adminStorageState: AdminStorageState = AdminStorageState(loading = false),
    onRefreshAdminStorage: () -> Unit = {},
    onFiles: () -> Unit,
    onShared: () -> Unit = {},
    onSearch: () -> Unit = {},
    onRecent: () -> Unit = {},
    onTrash: () -> Unit,
    onLogout: () -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
    ) {
        Text("KuraStorage", style = MaterialTheme.typography.headlineMedium)
        Text("Connection: ${connection?.route?.name ?: "UNKNOWN"}")
        AdminStoragePanel(adminStorageState, onRefreshAdminStorage, onTrash)
        Button(onClick = onFiles) { Text("My files") }
        Button(onClick = onShared) { Text("Shared") }
        Button(onClick = onSearch) { Text("Search") }
        Button(onClick = onRecent) { Text("Recent files") }
        Button(onClick = onTrash) { Text("Trash") }
        Button(onClick = onLogout) { Text("Log out") }
    }
}

private fun settingsRoute(
    shareId: String,
    targetId: String,
    type: FileEntryType,
    name: String,
): String = "${AppDestination.SHARING_SETTINGS.route}/$shareId/$targetId/${type.name}/${Uri.encode(name)}"

private fun entryRoute(
    id: String,
    type: FileEntryType,
): String = "shared-entry/$id/${type.name}"

private suspend fun loadAllReceivedShares(repository: SharingRepository): List<ShareItem> {
    val result = mutableListOf<ShareItem>()
    var pageNumber = 1
    do {
        val page = repository.list(ShareScope.RECEIVED, page = pageNumber, pageSize = SharingRepository.DEFAULT_PAGE_SIZE)
        result += page.items
        pageNumber++
    } while (page.hasNextPage)
    return result
}
