import org.cyclonedx.gradle.CyclonedxDirectTask
import org.cyclonedx.model.Component
import org.gradle.api.tasks.TaskProvider
import org.gradle.api.tasks.testing.Test
import org.gradle.testing.jacoco.plugins.JacocoPluginExtension
import org.gradle.testing.jacoco.plugins.JacocoTaskExtension
import org.gradle.testing.jacoco.tasks.JacocoCoverageVerification
import org.gradle.testing.jacoco.tasks.JacocoReport

plugins {
    base
    jacoco
    id("org.cyclonedx.bom") version "3.4.1"
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.android.library) apply false
    alias(libs.plugins.kotlin.android) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    alias(libs.plugins.detekt)
    alias(libs.plugins.ktlint)
}

val kuraStorageVersion = providers.gradleProperty("kurastorage.versionName").orElse("development").get()

allprojects {
    group = "com.kurastorage"
    version = kuraStorageVersion
    dependencyLocking {
        lockAllConfigurations()
    }
    tasks.named<CyclonedxDirectTask>("cyclonedxDirectBom") {
        includeConfigs = listOf("releaseRuntimeClasspath")
        includeLicenseText = true
    }
}

project(":app").tasks.named<CyclonedxDirectTask>("cyclonedxDirectBom") {
    projectType = Component.Type.APPLICATION
}

subprojects {
    pluginManager.apply("io.gitlab.arturbosch.detekt")
    pluginManager.apply("org.jlleitschuh.gradle.ktlint")
    pluginManager.apply("jacoco")
    extensions.configure<JacocoPluginExtension> {
        toolVersion = "0.8.13"
    }
    tasks.withType<Test>().configureEach {
        extensions.configure<JacocoTaskExtension> {
            isIncludeNoLocationClasses = true
            excludes = listOf("jdk.internal.*")
        }
    }
}

val coverageModules =
    listOf(
        ":app",
        ":core-data",
        ":core-model",
        ":core-network",
        ":core-security",
        ":feature-auth",
        ":feature-connection",
        ":feature-files",
        ":feature-media",
        ":feature-search",
        ":feature-settings",
        ":feature-sharing",
    )

val coverageExecutionData =
    coverageModules.map { module ->
        project(module).layout.buildDirectory.file("outputs/unit_test_code_coverage/debugUnitTest/testDebugUnitTest.exec")
    }
val coverageSources =
    coverageModules.map { module ->
        project(module).layout.projectDirectory.dir("src/main/kotlin")
    }

fun coverageClasses(includes: List<String>) =
    coverageModules.flatMap { module ->
        val buildDirectory =
            project(module)
                .layout.buildDirectory
                .get()
                .asFile
        listOf(
            fileTree("$buildDirectory/tmp/kotlin-classes/debug") {
                include(includes)
                exclude("**/AssemblyMarker.class", "**/BuildConfig.class", "**/*ComposableSingletons*.*")
            },
            fileTree("$buildDirectory/intermediates/javac/debug/compileDebugJavaWithJavac/classes") {
                include(includes)
                exclude("**/BuildConfig.class")
            },
        )
    }

val domainApplicationIncludes =
    listOf(
        "com/kurastorage/core/model/**",
        "com/kurastorage/core/data/**",
        "com/kurastorage/core/network/**",
        "com/kurastorage/core/security/**",
        "com/kurastorage/feature/**/*Controller*.*",
        "com/kurastorage/feature/**/*ViewModel*.*",
    )
val domainApplicationExcludes =
    listOf(
        "com/kurastorage/core/data/**/*Android*.*",
        "com/kurastorage/core/data/**/KuraMedia*.*",
        "com/kurastorage/core/network/KuraStorageApi*.*",
        "com/kurastorage/core/network/ApiContracts*.*",
        "com/kurastorage/feature/media/pdf/PdfDocumentController*.*",
        "com/kurastorage/feature/media/player/AndroidMediaPlayerController*.*",
    )
val criticalMediaStateIncludes =
    listOf(
        "com/kurastorage/core/model/media/**",
        "com/kurastorage/core/network/media/MediaContracts*.*",
        "com/kurastorage/core/data/media/MediaRangeRequest*.*",
        "com/kurastorage/core/data/media/NetworkQualityContextResolver*.*",
        "com/kurastorage/core/data/media/TransferConfirmationPolicy*.*",
        "com/kurastorage/feature/media/MediaViewerController*.*",
        "com/kurastorage/feature/media/photo/PhotoViewerViewModel*.*",
        "com/kurastorage/feature/media/player/MediaPlayerViewModel*.*",
        "com/kurastorage/feature/media/player/PlayerCommandController*.*",
        "com/kurastorage/feature/settings/QualitySettingsViewModel*.*",
    )
val criticalMediaStateExcludes =
    listOf(
        // Transport-only request DTO; it does not perform a state transition.
        "com/kurastorage/core/model/media/MediaOpenRequest*.*",
    )

fun registerCoverageReport(
    taskName: String,
    includes: List<String>,
    excludes: List<String> = emptyList(),
) = tasks.register<JacocoReport>(taskName) {
    group = "verification"
    description = "Aggregates Android JVM line coverage for $taskName."
    dependsOn(coverageModules.map { "$it:testDebugUnitTest" })
    executionData.from(coverageExecutionData)
    sourceDirectories.from(coverageSources)
    classDirectories.from(
        coverageClasses(includes).map { classes -> classes.matching { exclude(excludes) } },
    )
    reports {
        html.required.set(true)
        html.outputLocation.set(layout.buildDirectory.dir("reports/jacoco/$taskName/html"))
        xml.required.set(true)
        xml.outputLocation.set(layout.buildDirectory.file("reports/jacoco/$taskName/report.xml"))
        csv.required.set(false)
    }
}

fun registerCoverageVerification(
    taskName: String,
    reportTask: TaskProvider<JacocoReport>,
    includes: List<String>,
    minimumRatio: String,
    excludes: List<String> = emptyList(),
) = tasks.register<JacocoCoverageVerification>(taskName) {
    group = "verification"
    description = "Enforces at least $minimumRatio Android JVM line coverage for $taskName."
    dependsOn(reportTask)
    executionData.from(coverageExecutionData)
    sourceDirectories.from(coverageSources)
    classDirectories.from(
        coverageClasses(includes).map { classes -> classes.matching { exclude(excludes) } },
    )
    violationRules {
        rule {
            limit {
                counter = "LINE"
                value = "COVEREDRATIO"
                minimum = minimumRatio.toBigDecimal()
            }
        }
    }
}

val domainApplicationCoverage =
    registerCoverageReport("androidDomainApplicationCoverage", domainApplicationIncludes, domainApplicationExcludes)
val criticalMediaStateCoverage =
    registerCoverageReport("androidCriticalMediaStateCoverage", criticalMediaStateIncludes, criticalMediaStateExcludes)
val domainApplicationCoverageVerification =
    registerCoverageVerification(
        "androidDomainApplicationCoverageVerification",
        domainApplicationCoverage,
        domainApplicationIncludes,
        "0.80",
        domainApplicationExcludes,
    )
val criticalMediaStateCoverageVerification =
    registerCoverageVerification(
        "androidCriticalMediaStateCoverageVerification",
        criticalMediaStateCoverage,
        criticalMediaStateIncludes,
        "0.95",
        criticalMediaStateExcludes,
    )

tasks.register("androidCoverageVerification") {
    group = "verification"
    description = "Runs all Android Domain/Application and critical-state coverage gates."
    dependsOn(domainApplicationCoverageVerification, criticalMediaStateCoverageVerification)
}
