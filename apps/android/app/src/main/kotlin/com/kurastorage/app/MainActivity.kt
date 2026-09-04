@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "TooGenericExceptionCaught",
    "TooManyFunctions",
)

package com.kurastorage.app

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.StrictMode
import android.provider.OpenableColumns
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.SharingRepository
import com.kurastorage.core.data.media.MediaContentDownloader
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.ShareScope
import com.kurastorage.core.model.SupportedTextMimeTypes
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.filePermissionCapabilities
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.SupportedMediaMimeTypes
import com.kurastorage.core.ui.AppDestination
import com.kurastorage.core.ui.KuraStorageTheme
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraCardVariant
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.icons.KuraFileType
import com.kurastorage.core.ui.icons.KuraFileTypeIcon
import com.kurastorage.feature.activity.ActivityScreen
import com.kurastorage.feature.activity.ActivityViewModel
import com.kurastorage.feature.auth.AuthScreen
import com.kurastorage.feature.auth.AuthViewModel
import com.kurastorage.feature.backup.BackupOverviewScreen
import com.kurastorage.feature.backup.BackupOverviewViewModel
import com.kurastorage.feature.backup.BackupRulesScreen
import com.kurastorage.feature.backup.BackupRulesViewModel
import com.kurastorage.feature.backup.BackupSettingsScreen
import com.kurastorage.feature.backup.BackupWifiScreen
import com.kurastorage.feature.backup.BackupWifiViewModel
import com.kurastorage.feature.backup.SelectedBackupDestination
import com.kurastorage.feature.backup.SelectedBackupSource
import com.kurastorage.feature.connection.ConnectionScreen
import com.kurastorage.feature.connection.ConnectionViewModel
import com.kurastorage.feature.files.AdminStorageState
import com.kurastorage.feature.files.AdminStorageViewModel
import com.kurastorage.feature.files.FileBrowserScreen
import com.kurastorage.feature.files.FileBrowserViewModel
import com.kurastorage.feature.media.MediaViewerController
import com.kurastorage.feature.media.pdf.PdfViewerScreen
import com.kurastorage.feature.media.pdf.PdfViewerViewModel
import com.kurastorage.feature.media.photo.PhotoViewerScreen
import com.kurastorage.feature.media.photo.PhotoViewerViewModel
import com.kurastorage.feature.media.thumbnail.FileThumbnail
import com.kurastorage.feature.search.EntryOrganizationScreen
import com.kurastorage.feature.search.EntryOrganizationViewModel
import com.kurastorage.feature.search.FavoritesScreen
import com.kurastorage.feature.search.FavoritesViewModel
import com.kurastorage.feature.search.RecentFilesScreen
import com.kurastorage.feature.search.RecentFilesViewModel
import com.kurastorage.feature.search.SearchFilterOption
import com.kurastorage.feature.search.SearchScreen
import com.kurastorage.feature.search.SearchViewModel
import com.kurastorage.feature.search.TagsScreen
import com.kurastorage.feature.search.TagsViewModel
import com.kurastorage.feature.settings.QualitySettingsScreen
import com.kurastorage.feature.settings.QualitySettingsViewModel
import com.kurastorage.feature.settings.SettingsHubScreen
import com.kurastorage.feature.sharing.SharingListViewModel
import com.kurastorage.feature.sharing.SharingScreen
import com.kurastorage.feature.sharing.SharingSettingsScreen
import com.kurastorage.feature.sharing.SharingSettingsViewModel
import com.kurastorage.feature.text.TextEditorScreen
import com.kurastorage.feature.text.TextEditorViewModel
import com.kurastorage.feature.text.VersionHistoryScreen
import com.kurastorage.feature.text.VersionHistoryViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {
    private lateinit var container: ServiceContainer
    private val connectionViewModel: ConnectionViewModel by viewModels {
        simpleViewModelFactory { ConnectionViewModel(container.connectionDetector) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        if (BuildConfig.DEBUG) enableStrictMode()
        super.onCreate(savedInstanceState)
        container = (application as KuraStorageApplication).container
        setContent {
            KuraStorageTheme {
                KuraStorageApp(container, connectionViewModel)
            }
        }
    }

    private fun enableStrictMode() {
        StrictMode.setThreadPolicy(
            StrictMode.ThreadPolicy
                .Builder()
                .detectAll()
                .penaltyLog()
                .build(),
        )
        StrictMode.setVmPolicy(
            StrictMode.VmPolicy
                .Builder()
                .detectActivityLeaks()
                .detectLeakedClosableObjects()
                .detectLeakedRegistrationObjects()
                .penaltyLog()
                .build(),
        )
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
    var backupUi by remember { mutableStateOf<BackupUiServices?>(null) }
    var selectedBackupSource by remember { mutableStateOf<SelectedBackupSource?>(null) }
    var selectedBackupDestination by remember { mutableStateOf<SelectedBackupDestination?>(null) }
    val mediaContexts = remember { MediaNavigationContextStore() }

    DisposableEffect(services) {
        val activeServices = services
        onDispose { activeServices?.close() }
    }

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

    LaunchedEffect(backupUi) {
        val backup = backupUi ?: return@LaunchedEffect
        val enabledRules =
            backup.rules
                .observe(backup.scope)
                .first()
                .filter { it.enabled }
        backup.coordinator.onAppStarted(backup.scope, enabledRules.map { it.id })
        enabledRules.filter { it.sourceType == BackupSourceType.SAF_TREE }.forEach {
            backup.coordinator.scheduleSafPeriodic(backup.scope, it.id)
        }
    }

    KuraStorageAppShell(navController = navController) { contentPadding ->
        NavHost(
            navController = navController,
            startDestination = AppDestination.CONNECTION.route,
            modifier = Modifier.padding(contentPadding),
        ) {
            composable(AppDestination.CONNECTION.route) {
                ConnectionScreen(
                    state = connectionState,
                    onRecheck = connectionViewModel::check,
                    onConnected = { state ->
                        val replaceSession = connected?.route != state.route || services == null
                        if (replaceSession) {
                            services?.close()
                            mediaContexts.clear()
                            backupUi = null
                            connected = state
                            services = container.sessionServices(state.route)
                        }
                        navController.navigate(AppDestination.AUTHENTICATION.route) {
                            if (replaceSession) popUpTo(0)
                            launchSingleTop = true
                        }
                    },
                )
            }
            composable(AppDestination.AUTHENTICATION.route) {
                val current = services
                val route = connected?.route
                if (route == null || current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val authRepository = current.authentication
                val authViewModel: AuthViewModel =
                    viewModel(
                        key = "auth-${current.sessionId}",
                        factory =
                            simpleViewModelFactory {
                                AuthViewModel(route, Build.MODEL, authRepository)
                            },
                    )
                val authState by authViewModel.state.collectAsStateWithLifecycle()
                AuthScreen(
                    state = authState,
                    onSubmit = authViewModel::submit,
                    onRetry = navController::navigateToConnection,
                    onAuthenticated = {
                        backupUi = container.backupUiServices(checkNotNull(services))
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
                val backup = backupUi
                if (current == null || authRepository == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                if (route == null || backup == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val homeViewModel: HomeViewModel =
                    viewModel(
                        key = "home-${current.sessionId}-${backup.scope.value}",
                        factory =
                            simpleViewModelFactory {
                                HomeViewModel(current.recentFiles, backup.state, backup.scope)
                            },
                    )
                val homeState by homeViewModel.state.collectAsStateWithLifecycle()
                val storageViewModel =
                    if (authRepository.role() == UserRole.ADMIN) {
                        viewModel<AdminStorageViewModel>(
                            key = "home-storage-${current.sessionId}",
                            factory = simpleViewModelFactory { AdminStorageViewModel(current.adminStorage) },
                        )
                    } else {
                        null
                    }
                val storageState = AdminStorageStateFor(storageViewModel)
                HomeScreen(
                    connection = connected,
                    state = homeState,
                    adminStorageState = storageState,
                    onRefreshAdminStorage = { storageViewModel?.refresh() },
                    onRefreshRecent = homeViewModel::refreshRecent,
                    onFiles = { navController.navigateToTopLevel(TopLevelDestination.FILES) },
                    onShared = { navController.navigateToTopLevel(TopLevelDestination.SHARING) },
                    onSearch = { navController.navigateToTopLevel(TopLevelDestination.SEARCH) },
                    onRecent = { navController.navigate(AppDestination.RECENT_FILES.route) },
                    onCategory = { category -> navController.navigate(searchCategoryRoute(category)) },
                    onOpenRecent = { recent ->
                        val item = recent.metadata
                        if (item.entryType != FileEntryType.UNKNOWN && item.status == FileEntryStatus.ACTIVE) {
                            navController.navigate(entryRoute(item.id, item.entryType))
                        }
                    },
                    onActivity = { navController.navigate(AppDestination.ACTIVITY.route) },
                    onFavorites = { navController.navigate(AppDestination.FAVORITES.route) },
                    onTags = { navController.navigate(AppDestination.TAGS.route) },
                    onTrash = { navController.navigate(AppDestination.TRASH.route) },
                )
            }
            composable(AppDestination.SETTINGS.route) {
                val current = services
                val route = connected?.route
                if (current == null || route == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val logoutViewModel: AuthViewModel =
                    viewModel(
                        key = "logout-${current.sessionId}",
                        factory = simpleViewModelFactory { AuthViewModel(route, Build.MODEL, current.authentication) },
                    )
                SettingsHubScreen(
                    isAdmin = current.authentication.role() == UserRole.ADMIN,
                    onConnection = { navController.navigate(AppDestination.CONNECTION.route) },
                    onMediaSettings = { navController.navigate(AppDestination.MEDIA_SETTINGS.route) },
                    onBackupSettings = { navController.navigate(AppDestination.BACKUP_SETTINGS.route) },
                    onActivity = { navController.navigate(AppDestination.ACTIVITY.route) },
                    onTrash = { navController.navigate(AppDestination.TRASH.route) },
                    onLogout = {
                        logoutViewModel.logout {
                            services?.close()
                            mediaContexts.clear()
                            services = null
                            backupUi = null
                            connected = null
                            navController.navigateToConnection()
                        }
                    },
                )
            }
            composable(AppDestination.MEDIA_SETTINGS.route) {
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val settingsViewModel: QualitySettingsViewModel =
                    viewModel(
                        key = "media-quality-settings-${current.sessionId}",
                        factory = simpleViewModelFactory { QualitySettingsViewModel(current.qualityPreferences) },
                    )
                val settingsState by settingsViewModel.state.collectAsStateWithLifecycle()
                QualitySettingsScreen(
                    state = settingsState,
                    onSelect = settingsViewModel::update,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppDestination.BACKUP_SETTINGS.route) {
                if (backupUi == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                BackupSettingsScreen(
                    onOverview = { navController.navigate(AppDestination.BACKUP_OVERVIEW.route) },
                    onRules = { navController.navigate(AppDestination.BACKUP_RULES.route) },
                    onWifi = { navController.navigate(AppDestination.BACKUP_WIFI.route) },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppDestination.BACKUP_OVERVIEW.route) {
                val current = services ?: return@composable
                val backup = backupUi ?: return@composable
                val model: BackupOverviewViewModel =
                    viewModel(
                        key = "backup-overview-${current.sessionId}-${backup.scope.value}",
                        factory =
                            simpleViewModelFactory {
                                BackupOverviewViewModel(backup.scope, backup.rules, backup.state, backup.coordinator)
                            },
                    )
                val state by model.state.collectAsStateWithLifecycle()
                BackupOverviewScreen(
                    state = state,
                    onRunNow = model::runNow,
                    onPause = model::setPaused,
                    onRetry = { model.retry(it.id) },
                    onRetryAll = model::retryAllFailures,
                    onLoadMore = model::loadMore,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppDestination.BACKUP_RULES.route) {
                val current = services ?: return@composable
                val backup = backupUi ?: return@composable
                val context = androidx.compose.ui.platform.LocalContext.current
                val model: BackupRulesViewModel =
                    viewModel(
                        key = "backup-rules-${current.sessionId}-${backup.scope.value}",
                        factory = simpleViewModelFactory { BackupRulesViewModel(backup.scope, backup.rules) },
                    )
                val state by model.state.collectAsStateWithLifecycle()
                val safPicker =
                    rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
                        if (uri != null) {
                            runCatching {
                                context.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
                            }
                            selectedBackupSource = SelectedBackupSource(uri.toString(), uri.lastPathSegment ?: "Device folder")
                        }
                    }
                val mediaPermission =
                    rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
                        if (!granted) {
                            Toast
                                .makeText(
                                    context,
                                    "Media permission is required for this backup source.",
                                    Toast.LENGTH_LONG,
                                ).show()
                        }
                    }
                BackupRulesScreen(
                    state = state,
                    selectedSource = selectedBackupSource,
                    selectedDestination = selectedBackupDestination,
                    onPickSafSource = { safPicker.launch(null) },
                    onPickDestination = {
                        navController.navigate(AppDestination.BACKUP_DESTINATION.route)
                    },
                    onRequestMediaPermission = { type -> mediaPermission.launch(mediaPermissionFor(type)) },
                    onSave = { input, existing, onSaved -> model.save(input, existing, onSaved) },
                    onToggle = model::setEnabled,
                    onDelete = { model.delete(it.id) },
                    onSelectionsConsumed = {
                        selectedBackupSource = null
                        selectedBackupDestination = null
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppDestination.BACKUP_WIFI.route) {
                val current = services ?: return@composable
                val backup = backupUi ?: return@composable
                val context = androidx.compose.ui.platform.LocalContext.current
                val model: BackupWifiViewModel =
                    viewModel(
                        key = "backup-wifi-${current.sessionId}-${backup.scope.value}",
                        factory = simpleViewModelFactory { BackupWifiViewModel(backup.scope, backup.wifi) },
                    )
                val state by model.state.collectAsStateWithLifecycle()
                val permissions =
                    rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { model.refreshCurrent() }
                BackupWifiScreen(
                    state = state,
                    onRefresh = model::refreshCurrent,
                    onRequestPermission = { permissions.launch(it.toTypedArray()) },
                    onRegister = model::register,
                    onSave = model::save,
                    onDelete = model::delete,
                    onOpenAppSettings = {
                        context.startActivity(
                            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}")),
                        )
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppDestination.BACKUP_DESTINATION.route) {
                val current = services ?: return@composable
                BackupDestinationPicker(
                    files = current.files,
                    sharing = current.sharing,
                    onSelected = {
                        selectedBackupDestination = it
                        navController.popBackStack()
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppDestination.FILES.route) {
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val filesViewModel: FileBrowserViewModel =
                    viewModel(
                        key = "files-${current.sessionId}",
                        factory =
                            simpleViewModelFactory {
                                FileBrowserViewModel(current.files, current.transfers, recentFiles = current.recentFiles)
                            },
                    )
                val storageViewModel =
                    if (current.authentication.role() == UserRole.ADMIN) {
                        viewModel<AdminStorageViewModel>(
                            key = "files-storage-${current.sessionId}",
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
                    onSearch = { navController.navigate(AppDestination.SEARCH.route) },
                    onExit = { navController.popBackStack() },
                    onShare = { entry -> navController.navigate(settingsRoute("new", entry.id, entry.entryType, entry.name)) },
                    onOrganization = { entryId -> navController.navigate(organizationRoute(entryId)) },
                    media = current.media,
                    onOpenMedia = { entry, entries ->
                        mediaRoute(entry, entries, mediaContexts)?.let(navController::navigate) != null
                    },
                    onOpenText = { entry -> textRoute(entry)?.let(navController::navigate) != null },
                    requestedDetailsId = mediaContexts.requestedDetailsId,
                    onDetailsConsumed = mediaContexts::consumeDetails,
                )
            }
            composable(AppDestination.TRASH.route) {
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val trashViewModel: FileBrowserViewModel =
                    viewModel(
                        key = "trash-${current.sessionId}",
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
                    onOrganization = {},
                    media = null,
                )
            }
            composable(AppDestination.SHARING.route) {
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val sharingViewModel: SharingListViewModel =
                    viewModel(
                        key = "sharing-${current.sessionId}",
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
            composable(
                route = "${AppDestination.SEARCH.route}?category={category}",
                arguments =
                    listOf(
                        navArgument("category") {
                            type = NavType.StringType
                            nullable = true
                            defaultValue = null
                        },
                    ),
            ) { backStackEntry ->
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val searchViewModel: SearchViewModel =
                    viewModel(
                        key = "search-${current.sessionId}",
                        factory = simpleViewModelFactory { SearchViewModel(current.search, current.files::detail) },
                    )
                val state by searchViewModel.state.collectAsStateWithLifecycle()
                val requestedCategory =
                    backStackEntry.arguments
                        ?.getString("category")
                        ?.let { value -> SearchFileCategory.entries.firstOrNull { it.name == value } }
                LaunchedEffect(requestedCategory) {
                    if (requestedCategory != null) {
                        searchViewModel.updateInput(state.input.copy(fileCategory = requestedCategory))
                        searchViewModel.search()
                    }
                }
                var ownerOptions by remember { mutableStateOf(emptyList<SearchFilterOption>()) }
                var shareOptions by remember { mutableStateOf(emptyList<SearchFilterOption>()) }
                var tagOptions by remember { mutableStateOf(emptyList<com.kurastorage.core.model.TagItem>()) }
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
                        tagOptions = current.organization.listTags()
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
                    tagOptions = tagOptions,
                    onManageTags = { navController.navigate(AppDestination.TAGS.route) },
                    categoryMode = requestedCategory,
                    onClear = {
                        searchViewModel.updateInput(
                            com.kurastorage.core.model
                                .SearchInput(),
                        )
                    },
                    onFavorites = { navController.navigate(AppDestination.FAVORITES.route) },
                )
            }
            composable(AppDestination.RECENT_FILES.route) {
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val recentViewModel: RecentFilesViewModel =
                    viewModel(
                        key = "recent-${current.sessionId}",
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
            composable(AppDestination.ACTIVITY.route) {
                val current = services
                if (current == null) {
                    navController.navigateToConnection()
                    return@composable
                }
                val activityViewModel: ActivityViewModel =
                    viewModel(
                        key = activityViewModelKey(current.sessionId),
                        factory = simpleViewModelFactory { ActivityViewModel(current.activity) },
                    )
                val state by activityViewModel.state.collectAsStateWithLifecycle()
                ActivityScreen(
                    state = state,
                    onBack = { navController.popBackStack() },
                    onRefresh = activityViewModel::refresh,
                    onFilter = activityViewModel::selectFilter,
                    onLoadMore = activityViewModel::loadMore,
                    onOpenTarget = { item ->
                        activityTargetRoute(item)?.let(navController::navigate)
                    },
                )
            }
            composable(AppDestination.FAVORITES.route) {
                val current = services ?: return@composable
                val favoritesViewModel: FavoritesViewModel =
                    viewModel(
                        key = "favorites-${current.sessionId}",
                        factory = simpleViewModelFactory { FavoritesViewModel(current.organization, current.files::detail) },
                    )
                val state by favoritesViewModel.state.collectAsStateWithLifecycle()
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
                FavoritesScreen(
                    state,
                    onBack = { navController.popBackStack() },
                    onRefresh = {
                        favoritesViewModel.refresh()
                        shareOptionsGeneration++
                    },
                    onLoadMore = favoritesViewModel::loadMore,
                    onOpen = { item ->
                        favoritesViewModel.open(item) { entry -> navController.navigate(entryRoute(entry.id, entry.entryType)) }
                    },
                    shareOptions = shareOptions,
                )
            }
            composable(AppDestination.TAGS.route) {
                val current = services ?: return@composable
                val tagsViewModel: TagsViewModel =
                    viewModel(
                        key = "tags-${current.sessionId}",
                        factory = simpleViewModelFactory { TagsViewModel(current.organization) },
                    )
                val state by tagsViewModel.state.collectAsStateWithLifecycle()
                TagsScreen(
                    state,
                    onBack = { navController.popBackStack() },
                    onRefresh = tagsViewModel::refresh,
                    onCreate = tagsViewModel::create,
                    onRename = tagsViewModel::rename,
                    onDelete = tagsViewModel::delete,
                    onInput = tagsViewModel::input,
                    onConfirm = tagsViewModel::confirm,
                    onDismiss = tagsViewModel::dismiss,
                )
            }
            composable(
                route = "${AppDestination.ENTRY_ORGANIZATION.route}/{entryId}",
                arguments = listOf(navArgument("entryId") { type = NavType.StringType }),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val entryId = checkNotNull(backStackEntry.arguments?.getString("entryId"))
                val organizationViewModel: EntryOrganizationViewModel =
                    viewModel(
                        key = "organization-$entryId-${current.sessionId}",
                        factory =
                            simpleViewModelFactory {
                                EntryOrganizationViewModel(
                                    entryId,
                                    current.organization,
                                    current.files::detail,
                                )
                            },
                    )
                val state by organizationViewModel.state.collectAsStateWithLifecycle()
                EntryOrganizationScreen(
                    state,
                    onBack = { navController.popBackStack() },
                    onRefresh = organizationViewModel::refresh,
                    onToggleFavorite = organizationViewModel::toggleFavorite,
                    onToggleTag = organizationViewModel::toggleTag,
                    onManageTags = { navController.navigate(AppDestination.TAGS.route) },
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
                        key = "shared-entry-$entryId-${current.sessionId}",
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
                    onOrganization = { entryId -> navController.navigate(organizationRoute(entryId)) },
                    media = current.media,
                    onOpenMedia = { entry, entries ->
                        mediaRoute(entry, entries, mediaContexts)?.let(navController::navigate) != null
                    },
                    onOpenText = { entry -> textRoute(entry)?.let(navController::navigate) != null },
                    requestedDetailsId = mediaContexts.requestedDetailsId,
                    onDetailsConsumed = mediaContexts::consumeDetails,
                )
            }
            composable(
                route = "${AppDestination.TEXT_EDITOR.route}/{fileId}",
                arguments = listOf(navArgument("fileId") { type = NavType.StringType }),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val fileId = checkNotNull(backStackEntry.arguments?.getString("fileId"))
                val context = androidx.compose.ui.platform.LocalContext.current
                val scope = rememberCoroutineScope()
                val textViewModel: TextEditorViewModel =
                    viewModel(
                        key = textEditorViewModelKey(fileId, current.sessionId),
                        factory =
                            savedStateViewModelFactory { handle ->
                                TextEditorViewModel(fileId, current.files, current.textFiles, handle)
                            },
                    )
                val state by textViewModel.state.collectAsStateWithLifecycle()
                var copyRequest by remember { mutableStateOf<TextCopyRequest?>(null) }
                val copyLauncher =
                    rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/plain")) { uri ->
                        val request = copyRequest
                        if (uri == null || request == null) {
                            copyRequest = null
                            return@rememberLauncherForActivityResult
                        }
                        scope.launch {
                            runCatching {
                                val bytes = request.content.toByteArray(Charsets.UTF_8)
                                withContext(Dispatchers.IO) {
                                    checkNotNull(context.contentResolver.openOutputStream(uri, "w")).use { it.write(bytes) }
                                }
                                val operation =
                                    current.transfers.newUpload(
                                        uri.toString(),
                                        request.parentId,
                                        request.name,
                                        bytes.size.toLong(),
                                        request.mimeType,
                                    )
                                current.transfers.upload(operation).collect { event ->
                                    when (event) {
                                        is TransferEvent.UploadCompleted ->
                                            Toast.makeText(context, "Copy uploaded", Toast.LENGTH_SHORT).show()
                                        is TransferEvent.Failed ->
                                            Toast.makeText(context, "Copy upload failed", Toast.LENGTH_LONG).show()
                                        else -> Unit
                                    }
                                }
                            }.onFailure {
                                Toast.makeText(context, "Copy upload failed", Toast.LENGTH_LONG).show()
                            }
                            copyRequest = null
                        }
                    }
                TextEditorScreen(
                    state = state,
                    onBack = { navController.popBackStack() },
                    onRequestExit = textViewModel::requestExit,
                    onDismissDiscard = textViewModel::dismissDiscardConfirmation,
                    onDiscard = textViewModel::discardChanges,
                    onBeginEdit = textViewModel::beginEditing,
                    onDraftChange = textViewModel::updateDraft,
                    onSave = textViewModel::save,
                    onReloadConflict = textViewModel::reloadAfterConflict,
                    onSaveAsCopy = { content ->
                        val file = state.file
                        val parentId = file?.parentId
                        if (file == null || parentId == null) {
                            Toast.makeText(context, "Destination folder is unavailable", Toast.LENGTH_LONG).show()
                        } else {
                            val extension = file.name.substringAfterLast('.', "txt")
                            val baseName = file.name.substringBeforeLast('.', file.name)
                            copyRequest = TextCopyRequest(content, parentId, "$baseName-copy.$extension", file.mimeType ?: "text/plain")
                            copyLauncher.launch(copyRequest?.name ?: "text-copy.txt")
                        }
                    },
                    onHistory = { navController.navigate("${AppDestination.TEXT_HISTORY.route}/$fileId") },
                    onReload = textViewModel::load,
                    onSaveAndExit = textViewModel::saveAndExit,
                    onExitAfterSaveConsumed = textViewModel::consumeExitAfterSave,
                    onEndEdit = textViewModel::endEditing,
                )
            }
            composable(
                route = "${AppDestination.TEXT_HISTORY.route}/{fileId}",
                arguments = listOf(navArgument("fileId") { type = NavType.StringType }),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val fileId = checkNotNull(backStackEntry.arguments?.getString("fileId"))
                val historyViewModel: VersionHistoryViewModel =
                    viewModel(
                        key = "text-history-$fileId-${current.sessionId}",
                        factory = simpleViewModelFactory { VersionHistoryViewModel(fileId, current.files, current.textFiles) },
                    )
                val state by historyViewModel.state.collectAsStateWithLifecycle()
                VersionHistoryScreen(
                    state = state,
                    onBack = { navController.popBackStack() },
                    onRefresh = historyViewModel::refresh,
                    onLoadMore = historyViewModel::loadMore,
                    onPreview = historyViewModel::preview,
                    onDismissPreview = historyViewModel::dismissPreview,
                    onRequestRestore = historyViewModel::requestRestore,
                    onDismissRestore = historyViewModel::dismissRestore,
                    onConfirmRestore = historyViewModel::confirmRestore,
                )
            }
            composable(
                route = "${AppDestination.PHOTO_VIEWER.route}/{contextId}/{fileId}",
                arguments =
                    listOf(
                        navArgument("contextId") { type = NavType.StringType },
                        navArgument("fileId") { type = NavType.StringType },
                    ),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val route = connected?.route ?: return@composable
                val contextId = checkNotNull(backStackEntry.arguments?.getString("contextId"))
                val fileId = checkNotNull(backStackEntry.arguments?.getString("fileId"))
                val photoViewModel: PhotoViewerViewModel =
                    viewModel(
                        key = "photo-$contextId-$fileId-${current.sessionId}-${current.media.scopeId}",
                        factory =
                            simpleViewModelFactory {
                                PhotoViewerViewModel(
                                    fileId,
                                    mediaContexts.fileIds(contextId),
                                    current.files,
                                    MediaViewerController(
                                        current.media.repository,
                                        current.media.qualityPreferences,
                                        current.media.contextResolver,
                                        current.media.confirmationPolicy,
                                        route,
                                        current.media.coroutineScope,
                                    ),
                                )
                            },
                    )
                val photoState by photoViewModel.state.collectAsStateWithLifecycle()
                val context = androidx.compose.ui.platform.LocalContext.current
                val downloadScope = rememberCoroutineScope()
                var pendingMediaDownload by remember { mutableStateOf<MediaDownloadSelection?>(null) }
                val mediaDownloadPicker =
                    rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("*/*")) { uri ->
                        val selection = pendingMediaDownload
                        pendingMediaDownload = null
                        if (uri != null && selection != null) {
                            downloadScope.launch {
                                val succeeded =
                                    runCatching {
                                        downloadMedia(context, current.media.downloader, uri, selection)
                                    }.isSuccess
                                Toast
                                    .makeText(
                                        context,
                                        if (succeeded) "Download completed." else "Download failed.",
                                        Toast.LENGTH_LONG,
                                    ).show()
                            }
                        }
                    }
                PhotoViewerScreen(
                    state = photoState,
                    imageLoader = current.media.imageLoader,
                    scopeId = current.media.scopeId,
                    requestTicket = photoViewModel::requestTicket,
                    onImageReady = photoViewModel::contentReady,
                    onGenerating = photoViewModel::contentGenerating,
                    onImageFailed = photoViewModel::contentFailed,
                    onQuality = photoViewModel::selectQuality,
                    onConfirmOriginal = photoViewModel::confirmOriginal,
                    onPrevious = photoViewModel::previous,
                    onNext = photoViewModel::next,
                    onZoom = photoViewModel::setZoom,
                    onDetails = {
                        photoState.file?.id?.let(mediaContexts::requestDetails)
                        navController.popBackStack()
                    },
                    onDownload = {
                        val file = photoState.file
                        val ready = photoState.media?.loadState as? MediaLoadState.Ready
                        if (file != null && ready != null) {
                            pendingMediaDownload = MediaDownloadSelection(file.id, file.name, ready.source.variant)
                            mediaDownloadPicker.launch(file.name)
                        }
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(
                route = "${AppDestination.PDF_VIEWER.route}/{fileId}",
                arguments = listOf(navArgument("fileId") { type = NavType.StringType }),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val fileId = checkNotNull(backStackEntry.arguments?.getString("fileId"))
                val pdfViewModel: PdfViewerViewModel =
                    viewModel(
                        key = "pdf-$fileId-${current.sessionId}-${current.media.scopeId}",
                        factory =
                            simpleViewModelFactory {
                                PdfViewerViewModel(fileId, current.files, current.media.repository, current.media.temporaryPdfStore)
                            },
                    )
                val pdfState by pdfViewModel.state.collectAsStateWithLifecycle()
                val context = androidx.compose.ui.platform.LocalContext.current
                val downloadScope = rememberCoroutineScope()
                var pendingPdfDownload by remember { mutableStateOf<MediaDownloadSelection?>(null) }
                val pdfDownloadPicker =
                    rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/pdf")) { uri ->
                        val selection = pendingPdfDownload
                        pendingPdfDownload = null
                        if (uri != null && selection != null) {
                            downloadScope.launch {
                                val succeeded =
                                    runCatching {
                                        downloadMedia(context, current.media.downloader, uri, selection)
                                    }.isSuccess
                                Toast
                                    .makeText(
                                        context,
                                        if (succeeded) "Download completed." else "Download failed.",
                                        Toast.LENGTH_LONG,
                                    ).show()
                            }
                        }
                    }
                PdfViewerScreen(
                    state = pdfState,
                    onConfirm = pdfViewModel::confirm,
                    onPrevious = pdfViewModel::previous,
                    onNext = pdfViewModel::next,
                    onPage = pdfViewModel::selectPage,
                    onZoom = pdfViewModel::setZoom,
                    onViewport = pdfViewModel::setViewport,
                    onDownload = {
                        pdfState.file?.let { file ->
                            pendingPdfDownload = MediaDownloadSelection(file.id, file.name, MediaVariant.ORIGINAL)
                            pdfDownloadPicker.launch(file.name)
                        }
                    },
                    onBack = { navController.popBackStack() },
                    onDisposeViewer = pdfViewModel::closeDocument,
                )
            }
            composable(
                route = "${AppDestination.VIDEO_PLAYER.route}/{contextId}/{fileId}",
                arguments =
                    listOf(
                        navArgument("contextId") { type = NavType.StringType },
                        navArgument("fileId") { type = NavType.StringType },
                    ),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val route = connected?.route ?: return@composable
                val fileId = checkNotNull(backStackEntry.arguments?.getString("fileId"))
                MediaPlayerRoute(
                    fileId = fileId,
                    kind = MediaKind.VIDEO,
                    current = current,
                    route = route,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(
                route = "${AppDestination.AUDIO_PLAYER.route}/{contextId}/{fileId}",
                arguments =
                    listOf(
                        navArgument("contextId") { type = NavType.StringType },
                        navArgument("fileId") { type = NavType.StringType },
                    ),
            ) { backStackEntry ->
                val current = services ?: return@composable
                val route = connected?.route ?: return@composable
                val fileId = checkNotNull(backStackEntry.arguments?.getString("fileId"))
                MediaPlayerRoute(
                    fileId = fileId,
                    kind = MediaKind.AUDIO,
                    current = current,
                    route = route,
                    onBack = { navController.popBackStack() },
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
                        key = "sharing-settings-${shareId ?: targetId}-${current.sessionId}",
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
}

@Composable
private fun FileRoute(
    viewModel: FileBrowserViewModel,
    adminStorageViewModel: AdminStorageViewModel?,
    trashMode: Boolean,
    onOpenTrash: () -> Unit,
    onSearch: () -> Unit = {},
    onExit: () -> Unit,
    onShare: (FileEntry) -> Unit,
    onOrganization: (String) -> Unit,
    media: MediaSessionScope?,
    onOpenMedia: (FileEntry, List<FileEntry>) -> Boolean = { _, _ -> false },
    onOpenText: (FileEntry) -> Boolean = { false },
    requestedDetailsId: String? = null,
    onDetailsConsumed: () -> Unit = {},
) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val adminStorageState = AdminStorageStateFor(adminStorageViewModel)
    var pendingDownload by remember { mutableStateOf<FileEntry?>(null) }
    LaunchedEffect(requestedDetailsId, state.entries) {
        val requested = requestedDetailsId ?: return@LaunchedEffect
        state.entries.firstOrNull { it.id == requested }?.let {
            viewModel.select(it)
            onDetailsConsumed()
        }
    }
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
        onOpen = { entry ->
            if (!onOpenText(entry) && !onOpenMedia(entry, state.entries)) viewModel.open(entry)
        },
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
        onOrganization = onOrganization,
        onOpenMedia = { entry ->
            if (onOpenMedia(entry, state.entries)) viewModel.dismissDetail()
        },
        onOpenText = { entry ->
            if (onOpenText(entry)) viewModel.dismissDetail()
        },
        onSearch = onSearch,
        thumbnail = { entry, modifier ->
            if (media == null) {
                Box(modifier, contentAlignment = Alignment.Center) {
                    KuraFileTypeIcon(KuraFileType.from(entry.mimeType, entry.entryType == FileEntryType.FOLDER))
                }
            } else {
                FileThumbnail(entry, media.scopeId, media.imageLoader, modifier)
            }
        },
    )
}

@Composable
private fun AdminStorageStateFor(viewModel: AdminStorageViewModel?): AdminStorageState {
    if (viewModel == null) return AdminStorageState(loading = false)
    val state by viewModel.state.collectAsStateWithLifecycle()
    return state
}

@Composable
private fun BackupDestinationPicker(
    files: FileRepository,
    sharing: SharingRepository,
    onSelected: (SelectedBackupDestination) -> Unit,
    onBack: () -> Unit,
) {
    var current by remember { mutableStateOf<FileEntry?>(null) }
    var personalRootId by remember { mutableStateOf<String?>(null) }
    var entries by remember { mutableStateOf<List<FileEntry>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    var showCreate by remember { mutableStateOf(false) }
    var createName by remember { mutableStateOf("") }
    var creating by remember { mutableStateOf(false) }
    var createError by remember { mutableStateOf<String?>(null) }
    val stack = remember { ArrayDeque<FileEntry?>().apply { addLast(null) } }
    val scope = rememberCoroutineScope()

    fun load(folder: FileEntry?) {
        loading = true
        error = null
        if (folder == null) personalRootId = null
        scope.launch {
            runCatching {
                val page = files.list(folder?.id, pageSize = 100)
                val loaded =
                    if (folder != null) {
                        page.items
                    } else {
                        val sharedFolders =
                            loadAllReceivedShares(sharing)
                                .filter { it.entryType == FileEntryType.FOLDER }
                                .mapNotNull { share -> runCatching { files.detail(share.targetEntryId) }.getOrNull() }
                        (page.items + sharedFolders).distinctBy(FileEntry::id)
                    }
                page.parentId to loaded
            }.onSuccess { (rootId, loaded) ->
                current = folder
                if (folder == null) personalRootId = rootId
                entries = loaded.filter { it.entryType == FileEntryType.FOLDER && it.status == FileEntryStatus.ACTIVE }
            }.onFailure {
                if (folder == null) personalRootId = null
                error = "Server folders could not be loaded."
            }
            loading = false
        }
    }
    LaunchedEffect(Unit) { load(null) }
    val writable =
        current?.let { filePermissionCapabilities(it.permission, it.permissionSource).canCreate }
            ?: (personalRootId != null)
    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                TextButton(onClick = {
                    if (stack.size > 1) {
                        stack.removeLast()
                        load(stack.last())
                    } else {
                        onBack()
                    }
                }) { Text("Back") }
                Text(
                    "Choose server folder",
                    modifier = Modifier.weight(1f).kuraHeading(),
                    style = MaterialTheme.typography.headlineSmall,
                )
                TextButton(onClick = { load(current) }, enabled = !loading && !creating) { Text("Refresh") }
            }
            Text(
                stack.joinToString(" / ") { it?.name ?: "My files" },
                style = MaterialTheme.typography.labelLarge,
            )
            KuraStatusPanel(
                title = if (writable) "Writable destination" else "Read-only destination",
                message =
                    "Only a currently writable folder can be selected. Access is checked again when the rule is saved and each backup runs.",
                status = if (writable) KuraStatus.SUCCESS else KuraStatus.WARNING,
            )
            error?.let {
                KuraStatusPanel("Server folders could not be loaded", it, KuraStatus.ERROR) {
                    TextButton(onClick = { load(current) }) { Text("Try again") }
                }
            }
            if (loading) {
                LinearProgressIndicator(Modifier.fillMaxWidth())
                Text("Loading folders…")
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                OutlinedButton(
                    onClick = {
                        createName = ""
                        createError = null
                        showCreate = true
                    },
                    enabled = writable && !loading && !creating,
                    modifier = Modifier.weight(1f),
                ) { Text("New folder") }
                Button(
                    onClick = {
                        if (current == null) {
                            personalRootId?.let { onSelected(SelectedBackupDestination(it, "My files")) }
                        } else {
                            val folder = requireNotNull(current)
                            onSelected(SelectedBackupDestination(folder.id, folder.name))
                        }
                    },
                    enabled = writable && !loading && !creating,
                    modifier = Modifier.weight(1f),
                ) { Text(if (current == null) "Use My files" else "Use this folder") }
            }
            LazyColumn(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                items(entries, key = { it.id }) { folder ->
                    val canCreate = filePermissionCapabilities(folder.permission, folder.permissionSource).canCreate
                    KuraCard(
                        variant = if (canCreate) KuraCardVariant.DEFAULT else KuraCardVariant.WARNING,
                        onClick = {
                            stack.addLast(folder)
                            load(folder)
                        },
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            KuraFileTypeIcon(KuraFileType.FOLDER)
                            Column(Modifier.weight(1f)) {
                                Text(folder.name, maxLines = 2, style = MaterialTheme.typography.titleMedium)
                                Text("${folder.owner.displayName} • ${folder.permission} (${folder.permissionSource})")
                            }
                            KuraStatusBadge(
                                if (canCreate) "Writable" else "Read only",
                                if (canCreate) KuraStatus.SUCCESS else KuraStatus.WARNING,
                            )
                        }
                    }
                }
            }
        }
    }
    if (showCreate) {
        AlertDialog(
            onDismissRequest = { if (!creating) showCreate = false },
            title = { Text("Create server folder") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                    Text("Create inside ${current?.name ?: "My files"}.")
                    OutlinedTextField(
                        value = createName,
                        onValueChange = {
                            createName = it
                            createError = null
                        },
                        label = { Text("Folder name") },
                        enabled = !creating,
                        singleLine = true,
                    )
                    createError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                    if (creating) LinearProgressIndicator(Modifier.fillMaxWidth())
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        if (creating || createName.isBlank()) return@Button
                        val parentId = current?.id ?: personalRootId ?: return@Button
                        creating = true
                        scope.launch {
                            runCatching { files.createFolder(parentId, createName) }
                                .onSuccess {
                                    showCreate = false
                                    createName = ""
                                    load(current)
                                }.onFailure {
                                    createError = "Folder could not be created. Check the name and current permission."
                                }
                            creating = false
                        }
                    },
                    enabled = createName.isNotBlank() && !creating,
                ) { Text(if (creating) "Creating…" else "Create") }
            },
            dismissButton = { TextButton(onClick = { showCreate = false }, enabled = !creating) { Text("Cancel") } },
        )
    }
}

private fun mediaPermissionFor(sourceType: BackupSourceType): String =
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        when (sourceType) {
            BackupSourceType.MEDIA_IMAGES -> Manifest.permission.READ_MEDIA_IMAGES
            BackupSourceType.MEDIA_VIDEOS -> Manifest.permission.READ_MEDIA_VIDEO
            BackupSourceType.MEDIA_AUDIO -> Manifest.permission.READ_MEDIA_AUDIO
            BackupSourceType.SAF_TREE -> error("SAF uses persistable document permission")
        }
    } else {
        Manifest.permission.READ_EXTERNAL_STORAGE
    }

private fun settingsRoute(
    shareId: String,
    targetId: String,
    type: FileEntryType,
    name: String,
): String = "${AppDestination.SHARING_SETTINGS.route}/$shareId/$targetId/${type.name}/${Uri.encode(name)}"

private fun searchCategoryRoute(category: SearchFileCategory): String = "${AppDestination.SEARCH.route}?category=${category.name}"

private fun entryRoute(
    id: String,
    type: FileEntryType,
): String = "shared-entry/$id/${type.name}"

internal fun activityTargetRoute(item: com.kurastorage.core.model.ActivityItem): String? {
    val targetEntryId = item.targetEntryId
    val type =
        when (item.targetType) {
            com.kurastorage.core.model.ActivityTargetType.FILE -> FileEntryType.FILE
            com.kurastorage.core.model.ActivityTargetType.FOLDER -> FileEntryType.FOLDER
            com.kurastorage.core.model.ActivityTargetType.UNKNOWN -> null
        }
    return if (targetEntryId != null && type != null) entryRoute(targetEntryId, type) else null
}

private fun organizationRoute(entryId: String): String = "${AppDestination.ENTRY_ORGANIZATION.route}/$entryId"

private data class MediaDownloadSelection(
    val fileId: String,
    val fileName: String,
    val variant: MediaVariant,
)

private suspend fun downloadMedia(
    context: android.content.Context,
    downloader: MediaContentDownloader,
    destination: Uri,
    selection: MediaDownloadSelection,
) {
    try {
        withContext(Dispatchers.IO) {
            val output = checkNotNull(context.contentResolver.openOutputStream(destination, "w"))
            output.use { downloader.download(selection.fileId, selection.variant, it) }
        }
    } catch (error: Throwable) {
        runCatching { context.contentResolver.delete(destination, null, null) }
        throw error
    }
}

internal fun mediaRoute(
    entry: FileEntry,
    entries: List<FileEntry>,
    contexts: MediaNavigationContextStore,
): String? {
    if (entry.entryType != FileEntryType.FILE || entry.status != com.kurastorage.core.model.FileEntryStatus.ACTIVE) return null
    return when (
        entry.mimeType
            ?.substringBefore(';')
            ?.trim()
            ?.lowercase()
    ) {
        "application/pdf" -> "${AppDestination.PDF_VIEWER.route}/${entry.id}"
        else ->
            if (SupportedMediaMimeTypes.isPhoto(entry.mimeType)) {
                val contextId =
                    contexts.register(
                        entries.filter { candidate ->
                            candidate.entryType == FileEntryType.FILE &&
                                candidate.status == com.kurastorage.core.model.FileEntryStatus.ACTIVE &&
                                SupportedMediaMimeTypes.isPhoto(candidate.mimeType)
                        },
                    )
                "${AppDestination.PHOTO_VIEWER.route}/$contextId/${entry.id}"
            } else if (SupportedMediaMimeTypes.isVideo(entry.mimeType)) {
                val contextId = contexts.register(entries.filter { SupportedMediaMimeTypes.isVideo(it.mimeType) })
                "${AppDestination.VIDEO_PLAYER.route}/$contextId/${entry.id}"
            } else if (SupportedMediaMimeTypes.isAudio(entry.mimeType)) {
                val contextId = contexts.register(entries.filter { SupportedMediaMimeTypes.isAudio(it.mimeType) })
                "${AppDestination.AUDIO_PLAYER.route}/$contextId/${entry.id}"
            } else {
                null
            }
    }
}

internal fun textRoute(entry: FileEntry): String? {
    if (
        entry.entryType != FileEntryType.FILE ||
        entry.status != com.kurastorage.core.model.FileEntryStatus.ACTIVE ||
        !SupportedTextMimeTypes.isSupported(entry.mimeType)
    ) {
        return null
    }
    return "${AppDestination.TEXT_EDITOR.route}/${entry.id}"
}

internal fun textEditorViewModelKey(
    fileId: String,
    sessionId: String,
): String = "text-editor-$fileId-$sessionId"

internal fun activityViewModelKey(sessionId: String): String = "activity-$sessionId"

private data class TextCopyRequest(
    val content: String,
    val parentId: String,
    val name: String,
    val mimeType: String,
)

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
