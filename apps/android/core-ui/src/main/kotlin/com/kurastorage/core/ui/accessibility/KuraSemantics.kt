package com.kurastorage.core.ui.accessibility

import androidx.compose.foundation.progressSemantics
import androidx.compose.runtime.Stable
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics

@Stable
fun Modifier.kuraHeading(): Modifier = semantics { heading() }

@Stable
fun Modifier.kuraSelected(isSelected: Boolean): Modifier = semantics { selected = isSelected }

@Stable
fun Modifier.kuraError(message: String): Modifier =
    semantics {
        error(message)
        liveRegion = LiveRegionMode.Assertive
    }

@Stable
fun Modifier.kuraLiveRegion(): Modifier = semantics { liveRegion = LiveRegionMode.Polite }

@Stable
fun Modifier.kuraProgress(progress: Float?): Modifier =
    if (progress == null) {
        kuraLiveRegion()
    } else {
        progressSemantics(progress.coerceIn(0f, 1f))
    }
