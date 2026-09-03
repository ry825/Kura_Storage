@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "MagicNumber")

package com.kurastorage.core.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

private val LightColors =
    lightColorScheme(
        primary = Color(0xFF0B354A),
        onPrimary = Color.White,
        primaryContainer = Color(0xFFD9EFF8),
        onPrimaryContainer = Color(0xFF062B3E),
        secondary = Color(0xFF456273),
        onSecondary = Color.White,
        secondaryContainer = Color(0xFFDCE9EF),
        onSecondaryContainer = Color(0xFF233F4E),
        background = Color(0xFFFCFAF7),
        onBackground = Color(0xFF122A39),
        surface = Color(0xFFFFFEFC),
        onSurface = Color(0xFF122A39),
        surfaceVariant = Color(0xFFF5F1EB),
        onSurfaceVariant = Color(0xFF52636D),
        outline = Color(0xFF817B74),
        outlineVariant = Color(0xFFD8D2CB),
        error = Color(0xFFB3261E),
        onError = Color.White,
        errorContainer = Color(0xFFF9DEDC),
        onErrorContainer = Color(0xFF410E0B),
    )

private val DarkColors =
    darkColorScheme(
        primary = Color(0xFF9DD8F0),
        onPrimary = Color(0xFF003547),
        primaryContainer = Color(0xFF164B61),
        onPrimaryContainer = Color(0xFFD0F0FC),
        secondary = Color(0xFFB4CBD7),
        onSecondary = Color(0xFF203640),
        secondaryContainer = Color(0xFF374D57),
        onSecondaryContainer = Color(0xFFD0E7F2),
        background = Color(0xFF101518),
        onBackground = Color(0xFFE4E9EC),
        surface = Color(0xFF171E22),
        onSurface = Color(0xFFE4E9EC),
        surfaceVariant = Color(0xFF20292E),
        onSurfaceVariant = Color(0xFFC0C9CE),
        outline = Color(0xFF9AA4AA),
        outlineVariant = Color(0xFF414B50),
        error = Color(0xFFFFB4AB),
        onError = Color(0xFF690005),
        errorContainer = Color(0xFF93000A),
        onErrorContainer = Color(0xFFFFDAD6),
    )

@Immutable
data class KuraSemanticColors(
    val success: Color,
    val onSuccess: Color,
    val successContainer: Color,
    val onSuccessContainer: Color,
    val warning: Color,
    val onWarning: Color,
    val warningContainer: Color,
    val onWarningContainer: Color,
    val info: Color,
    val onInfo: Color,
    val infoContainer: Color,
    val onInfoContainer: Color,
    val categoryPhoto: Color,
    val categoryVideo: Color,
    val categoryAudio: Color,
    val categoryDocument: Color,
)

private val LightSemanticColors =
    KuraSemanticColors(
        success = Color(0xFF0B6B3A),
        onSuccess = Color.White,
        successContainer = Color(0xFFD5F4DF),
        onSuccessContainer = Color(0xFF073D23),
        warning = Color(0xFF8A4E00),
        onWarning = Color.White,
        warningContainer = Color(0xFFFFE1B8),
        onWarningContainer = Color(0xFF4D2A00),
        info = Color(0xFF075E98),
        onInfo = Color.White,
        infoContainer = Color(0xFFD6E9FF),
        onInfoContainer = Color(0xFF003352),
        categoryPhoto = Color(0xFFC55A11),
        categoryVideo = Color(0xFF4B3A9A),
        categoryAudio = Color(0xFF0B6B3A),
        categoryDocument = Color(0xFF1769A0),
    )

private val DarkSemanticColors =
    KuraSemanticColors(
        success = Color(0xFF83D9A5),
        onSuccess = Color(0xFF00391D),
        successContainer = Color(0xFF07552D),
        onSuccessContainer = Color(0xFFB9F2CD),
        warning = Color(0xFFFFB95C),
        onWarning = Color(0xFF492900),
        warningContainer = Color(0xFF663C00),
        onWarningContainer = Color(0xFFFFDDB1),
        info = Color(0xFF9BCBFF),
        onInfo = Color(0xFF003354),
        infoContainer = Color(0xFF084C77),
        onInfoContainer = Color(0xFFD1E9FF),
        categoryPhoto = Color(0xFFFFB77D),
        categoryVideo = Color(0xFFC9BFFF),
        categoryAudio = Color(0xFF83D9A5),
        categoryDocument = Color(0xFF9BCBFF),
    )

@Immutable
data class KuraSpacing(
    val xxs: Dp = 4.dp,
    val xs: Dp = 8.dp,
    val sm: Dp = 12.dp,
    val md: Dp = 16.dp,
    val lg: Dp = 24.dp,
    val xl: Dp = 32.dp,
    val xxl: Dp = 48.dp,
)

@Immutable
data class KuraElevations(
    val flat: Dp = 0.dp,
    val raised: Dp = 1.dp,
    val floating: Dp = 4.dp,
)

private val LocalKuraColors = staticCompositionLocalOf { LightSemanticColors }
private val LocalKuraSpacing = staticCompositionLocalOf { KuraSpacing() }
private val LocalKuraElevations = staticCompositionLocalOf { KuraElevations() }

object KuraTheme {
    val colors: KuraSemanticColors
        @Composable
        @ReadOnlyComposable
        get() = LocalKuraColors.current

    val spacing: KuraSpacing
        @Composable
        @ReadOnlyComposable
        get() = LocalKuraSpacing.current

    val elevations: KuraElevations
        @Composable
        @ReadOnlyComposable
        get() = LocalKuraElevations.current
}

private val KuraShapes =
    Shapes(
        extraSmall = RoundedCornerShape(8.dp),
        small = RoundedCornerShape(12.dp),
        medium = RoundedCornerShape(16.dp),
        large = RoundedCornerShape(24.dp),
        extraLarge = RoundedCornerShape(32.dp),
    )

private val KuraTypography =
    Typography(
        displaySmall =
            TextStyle(
                fontFamily = FontFamily.Serif,
                fontWeight = FontWeight.Bold,
                fontSize = 36.sp,
                lineHeight = 44.sp,
            ),
        headlineSmall = TextStyle(fontWeight = FontWeight.Bold, fontSize = 24.sp, lineHeight = 32.sp),
        titleLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 20.sp, lineHeight = 28.sp),
        titleMedium = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 16.sp, lineHeight = 24.sp),
        bodyLarge = TextStyle(fontSize = 16.sp, lineHeight = 24.sp),
        bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 20.sp),
        bodySmall = TextStyle(fontSize = 12.sp, lineHeight = 16.sp),
        labelLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 14.sp, lineHeight = 20.sp),
    )

@Composable
internal fun KuraMaterialTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    CompositionLocalProvider(
        LocalKuraColors provides if (darkTheme) DarkSemanticColors else LightSemanticColors,
        LocalKuraSpacing provides KuraSpacing(),
        LocalKuraElevations provides KuraElevations(),
    ) {
        MaterialTheme(
            colorScheme = if (darkTheme) DarkColors else LightColors,
            typography = KuraTypography,
            shapes = KuraShapes,
            content = content,
        )
    }
}
