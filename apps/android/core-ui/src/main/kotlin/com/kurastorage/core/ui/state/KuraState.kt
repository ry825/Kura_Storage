@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongParameterList",
    "MatchingDeclarationName",
)

package com.kurastorage.core.ui.state

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraError
import com.kurastorage.core.ui.accessibility.kuraLiveRegion
import com.kurastorage.core.ui.accessibility.kuraProgress
import com.kurastorage.core.ui.components.KuraPrimaryButton
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel

enum class KuraStateKind {
    LOADING,
    EMPTY,
    RECOVERABLE_ERROR,
    BLOCKING_ERROR,
    PROGRESS,
}

@Composable
fun KuraStateView(
    kind: KuraStateKind,
    title: String,
    message: String,
    modifier: Modifier = Modifier,
    requestId: String? = null,
    progress: Float? = null,
    actionLabel: String? = null,
    onAction: (() -> Unit)? = null,
) {
    val semanticsModifier =
        when (kind) {
            KuraStateKind.RECOVERABLE_ERROR, KuraStateKind.BLOCKING_ERROR -> Modifier.kuraError(message)
            KuraStateKind.LOADING, KuraStateKind.PROGRESS -> Modifier.kuraProgress(progress)
            KuraStateKind.EMPTY -> Modifier.kuraLiveRegion()
        }
    val action: (@Composable () -> Unit)? =
        if (actionLabel != null && onAction != null) {
            { KuraPrimaryButton(actionLabel, onAction) }
        } else {
            null
        }
    val status =
        when (kind) {
            KuraStateKind.LOADING, KuraStateKind.PROGRESS -> KuraStatus.INFO
            KuraStateKind.EMPTY -> KuraStatus.NEUTRAL
            KuraStateKind.RECOVERABLE_ERROR, KuraStateKind.BLOCKING_ERROR -> KuraStatus.ERROR
        }
    Column(
        modifier = modifier.fillMaxWidth().then(semanticsModifier),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
    ) {
        if (kind == KuraStateKind.LOADING) CircularProgressIndicator()
        if (kind == KuraStateKind.PROGRESS) {
            if (progress == null) {
                LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
            } else {
                LinearProgressIndicator(progress = { progress.coerceIn(0f, 1f) }, modifier = Modifier.fillMaxWidth())
            }
        }
        KuraStatusPanel(title = title, message = message, status = status, action = action)
        requestId?.let { Text("Request ID: $it", style = MaterialTheme.typography.bodySmall) }
    }
}
