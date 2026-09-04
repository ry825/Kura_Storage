@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongParameterList",
    "MagicNumber",
    "MaxLineLength",
    "TooManyFunctions",
)

package com.kurastorage.core.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.accessibility.kuraSelected

private val MaximumContentWidth = 720.dp

enum class KuraCardVariant {
    DEFAULT,
    SELECTED,
    WARNING,
    DISABLED,
}

enum class KuraStatus {
    NEUTRAL,
    SUCCESS,
    WARNING,
    ERROR,
    INFO,
}

@Composable
fun KuraAppScaffold(
    modifier: Modifier = Modifier,
    topBar: @Composable () -> Unit = {},
    bottomBar: @Composable () -> Unit = {},
    floatingActionButton: @Composable () -> Unit = {},
    snackbarHost: @Composable () -> Unit = {},
    content: @Composable (PaddingValues) -> Unit,
) {
    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = topBar,
        bottomBar = bottomBar,
        floatingActionButton = floatingActionButton,
        snackbarHost = snackbarHost,
        containerColor = MaterialTheme.colorScheme.background,
        contentWindowInsets = WindowInsets.safeDrawing,
        content = content,
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun KuraTopAppBar(
    title: String,
    modifier: Modifier = Modifier,
    navigationIcon: @Composable () -> Unit = {},
    actions: @Composable RowScope.() -> Unit = {},
) {
    TopAppBar(
        title = { Text(title, modifier = Modifier.kuraHeading()) },
        modifier = modifier,
        navigationIcon = navigationIcon,
        actions = actions,
    )
}

@Composable
fun KuraScreenContent(
    modifier: Modifier = Modifier,
    verticalArrangement: Arrangement.Vertical = Arrangement.spacedBy(KuraTheme.spacing.md),
    content: @Composable ColumnScope.() -> Unit,
) {
    Box(
        modifier =
            modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.safeDrawing),
        contentAlignment = Alignment.TopCenter,
    ) {
        Column(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .widthIn(max = MaximumContentWidth)
                    .padding(horizontal = KuraTheme.spacing.md, vertical = KuraTheme.spacing.sm),
            verticalArrangement = verticalArrangement,
            content = content,
        )
    }
}

@Composable
fun KuraSectionHeader(
    text: String,
    modifier: Modifier = Modifier,
    action: (@Composable () -> Unit)? = null,
) {
    Row(
        modifier = modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
    ) {
        Text(
            text = text,
            modifier = Modifier.weight(1f).kuraHeading(),
            color = MaterialTheme.colorScheme.onBackground,
            style = MaterialTheme.typography.titleLarge,
        )
        action?.invoke()
    }
}

@Composable
fun KuraCard(
    modifier: Modifier = Modifier,
    variant: KuraCardVariant = KuraCardVariant.DEFAULT,
    onClick: (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    val colors =
        when (variant) {
            KuraCardVariant.DEFAULT -> CardDefaults.cardColors()
            KuraCardVariant.SELECTED -> CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)
            KuraCardVariant.WARNING -> CardDefaults.cardColors(containerColor = KuraTheme.colors.warningContainer)
            KuraCardVariant.DISABLED ->
                CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant,
                    contentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                    disabledContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                    disabledContentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                )
        }
    val borderColor =
        when (variant) {
            KuraCardVariant.SELECTED -> MaterialTheme.colorScheme.primary
            KuraCardVariant.WARNING -> KuraTheme.colors.warning
            else -> MaterialTheme.colorScheme.outlineVariant
        }
    androidx.compose.material3.Card(
        modifier =
            modifier
                .fillMaxWidth()
                .kuraSelected(variant == KuraCardVariant.SELECTED)
                .then(if (variant == KuraCardVariant.DISABLED) Modifier.semantics { disabled() } else Modifier)
                .then(
                    if (onClick == null || variant == KuraCardVariant.DISABLED) {
                        Modifier
                    } else {
                        Modifier.clickable(role = Role.Button, onClick = onClick)
                    },
                ),
        shape = MaterialTheme.shapes.medium,
        colors = colors,
        border = BorderStroke(1.dp, borderColor),
        elevation = CardDefaults.cardElevation(defaultElevation = KuraTheme.elevations.raised),
    ) {
        Column(
            modifier = Modifier.padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
            content = content,
        )
    }
}

@Composable
fun KuraListRow(
    headline: String,
    modifier: Modifier = Modifier,
    supportingText: String? = null,
    selected: Boolean = false,
    enabled: Boolean = true,
    onClick: (() -> Unit)? = null,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable () -> Unit)? = null,
) {
    Row(
        modifier =
            modifier
                .fillMaxWidth()
                .minimumInteractiveComponentSize()
                .kuraSelected(selected)
                .then(if (enabled) Modifier else Modifier.semantics { disabled() })
                .then(
                    if (onClick == null || !enabled) Modifier else Modifier.clickable(role = Role.Button, onClick = onClick),
                ).padding(horizontal = KuraTheme.spacing.sm, vertical = KuraTheme.spacing.xs),
        horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        leading?.invoke()
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xxs)) {
            Text(
                text = headline,
                color = if (enabled) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.titleMedium,
            )
            supportingText?.let { Text(it, style = MaterialTheme.typography.bodyMedium) }
        }
        trailing?.invoke()
    }
}

@Composable
fun KuraStatusBadge(
    label: String,
    status: KuraStatus,
    modifier: Modifier = Modifier,
) {
    val palette = statusPalette(status)
    Surface(
        modifier = modifier,
        shape = MaterialTheme.shapes.extraLarge,
        color = palette.container,
        contentColor = palette.onContainer,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = KuraTheme.spacing.sm, vertical = KuraTheme.spacing.xs),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(status.symbol, style = MaterialTheme.typography.labelLarge)
            Text(label, style = MaterialTheme.typography.labelLarge)
        }
    }
}

@Composable
fun KuraStatusPanel(
    title: String,
    message: String,
    status: KuraStatus,
    modifier: Modifier = Modifier,
    action: (@Composable () -> Unit)? = null,
) {
    val palette = statusPalette(status)
    Surface(
        modifier = modifier.fillMaxWidth(),
        color = palette.container,
        contentColor = palette.onContainer,
        shape = MaterialTheme.shapes.medium,
        border = BorderStroke(1.dp, palette.accent),
    ) {
        Row(
            modifier = Modifier.padding(KuraTheme.spacing.md),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(status.symbol, style = MaterialTheme.typography.titleLarge)
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xxs)) {
                Text(title, style = MaterialTheme.typography.titleMedium)
                Text(message, style = MaterialTheme.typography.bodyMedium)
            }
            action?.invoke()
        }
    }
}

@Composable
fun KuraPrimaryButton(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    Button(onClick = onClick, modifier = modifier.minimumInteractiveComponentSize(), enabled = enabled) { Text(label) }
}

@Composable
fun KuraSecondaryButton(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    FilledTonalButton(onClick = onClick, modifier = modifier.minimumInteractiveComponentSize(), enabled = enabled) {
        Text(label)
    }
}

@Composable
fun KuraDestructiveButton(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    Button(
        onClick = onClick,
        modifier = modifier.minimumInteractiveComponentSize(),
        enabled = enabled,
        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
    ) {
        Text(label)
    }
}

@Composable
fun KuraIconButton(
    contentDescription: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable () -> Unit,
) {
    require(contentDescription.isNotBlank()) { "Icon button content description must not be blank." }
    IconButton(
        onClick = onClick,
        modifier =
            modifier
                .sizeIn(minWidth = 48.dp, minHeight = 48.dp)
                .minimumInteractiveComponentSize()
                .semantics { this.contentDescription = contentDescription },
        enabled = enabled,
        content = content,
    )
}

@Composable
fun KuraTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isError: Boolean = false,
    supportingText: String? = null,
    visualTransformation: VisualTransformation = VisualTransformation.None,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        modifier = modifier.fillMaxWidth(),
        enabled = enabled,
        isError = isError,
        label = { Text(label) },
        supportingText = supportingText?.let { { Text(it) } },
        visualTransformation = visualTransformation,
        singleLine = true,
    )
}

@Composable
fun KuraPasswordField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isError: Boolean = false,
    supportingText: String? = null,
) {
    KuraTextField(
        value = value,
        onValueChange = onValueChange,
        label = label,
        modifier = modifier,
        enabled = enabled,
        isError = isError,
        supportingText = supportingText,
        visualTransformation = PasswordVisualTransformation(),
    )
}

@Composable
fun KuraSelectionField(
    label: String,
    value: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    OutlinedButton(
        onClick = onClick,
        modifier = modifier.fillMaxWidth().minimumInteractiveComponentSize(),
        enabled = enabled,
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(label, style = MaterialTheme.typography.labelLarge)
            Text(value, style = MaterialTheme.typography.bodyLarge)
        }
    }
}

@Composable
fun KuraSegmentedControl(
    labels: List<String>,
    selectedIndex: Int,
    onSelected: (Int) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    require(labels.isNotEmpty()) { "Segmented control requires at least one label." }
    val highFontScale = LocalDensity.current.fontScale >= 1.5f
    BoxWithConstraints(modifier = modifier.fillMaxWidth()) {
        val vertical = highFontScale || maxWidth < 360.dp
        val content: @Composable (Int, String, Modifier) -> Unit = { index, label, itemModifier ->
            val selected = index == selectedIndex
            if (selected) {
                FilledTonalButton(
                    onClick = { onSelected(index) },
                    modifier = itemModifier.kuraSelected(true),
                    enabled = enabled,
                ) { Text(label) }
            } else {
                OutlinedButton(
                    onClick = { onSelected(index) },
                    modifier = itemModifier.kuraSelected(false),
                    enabled = enabled,
                ) { Text(label) }
            }
        }
        if (vertical) {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                labels.forEachIndexed { index, label -> content(index, label, Modifier.fillMaxWidth()) }
            }
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                labels.forEachIndexed { index, label -> content(index, label, Modifier.weight(1f)) }
            }
        }
    }
}

@Composable
fun KuraAdaptiveActionLayout(
    actions: List<@Composable () -> Unit>,
    modifier: Modifier = Modifier,
) {
    require(actions.isNotEmpty()) { "Adaptive action layout requires at least one action." }
    val highFontScale = LocalDensity.current.fontScale >= 1.5f
    BoxWithConstraints(modifier = modifier.fillMaxWidth()) {
        if (highFontScale || maxWidth < 360.dp) {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                actions.forEach { action -> Box(Modifier.fillMaxWidth()) { action() } }
            }
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                actions.forEach { action -> Box(Modifier.weight(1f)) { action() } }
            }
        }
    }
}

@Composable
fun KuraConfirmationDialog(
    title: String,
    target: String,
    impact: String,
    confirmLabel: String,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier,
    destructive: Boolean = false,
    dismissLabel: String = "Cancel",
    confirmEnabled: Boolean = true,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        modifier = modifier,
        title = { Text(title, modifier = Modifier.kuraHeading()) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm)) {
                Text("Target", style = MaterialTheme.typography.labelLarge)
                Text(target)
                Text("Impact", style = MaterialTheme.typography.labelLarge)
                Text(impact)
            }
        },
        confirmButton = {
            if (destructive) {
                KuraDestructiveButton(confirmLabel, onConfirm, enabled = confirmEnabled)
            } else {
                KuraPrimaryButton(confirmLabel, onConfirm, enabled = confirmEnabled)
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(dismissLabel) } },
    )
}

private data class StatusPalette(
    val accent: Color,
    val container: Color,
    val onContainer: Color,
)

private val KuraStatus.symbol: String
    get() =
        when (this) {
            KuraStatus.NEUTRAL -> "•"
            KuraStatus.SUCCESS -> "✓"
            KuraStatus.WARNING -> "!"
            KuraStatus.ERROR -> "×"
            KuraStatus.INFO -> "i"
        }

@Composable
private fun statusPalette(status: KuraStatus): StatusPalette =
    when (status) {
        KuraStatus.NEUTRAL ->
            StatusPalette(
                MaterialTheme.colorScheme.outline,
                MaterialTheme.colorScheme.surfaceVariant,
                MaterialTheme.colorScheme.onSurfaceVariant,
            )
        KuraStatus.SUCCESS ->
            StatusPalette(
                KuraTheme.colors.success,
                KuraTheme.colors.successContainer,
                KuraTheme.colors.onSuccessContainer,
            )
        KuraStatus.WARNING ->
            StatusPalette(
                KuraTheme.colors.warning,
                KuraTheme.colors.warningContainer,
                KuraTheme.colors.onWarningContainer,
            )
        KuraStatus.ERROR ->
            StatusPalette(
                MaterialTheme.colorScheme.error,
                MaterialTheme.colorScheme.errorContainer,
                MaterialTheme.colorScheme.onErrorContainer,
            )
        KuraStatus.INFO -> StatusPalette(KuraTheme.colors.info, KuraTheme.colors.infoContainer, KuraTheme.colors.onInfoContainer)
    }
