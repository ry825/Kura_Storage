import org.apache.tools.ant.filters.ReplaceTokens

plugins {
    id("kurastorage.android.application")
    id("kurastorage.android.compose")
    id("kurastorage.android.test")
}

val apiHostname = providers.gradleProperty("kurastorage.apiHostname").orElse("api.kurastorage.example")
val lanApiAddress = providers.gradleProperty("kurastorage.lanApiAddress").orElse("192.0.2.10")
val zerotierApiAddress = providers.gradleProperty("kurastorage.zerotierApiAddress").orElse("198.51.100.10")
val rootCaCertificate = providers.gradleProperty("kurastorage.rootCaCertificate")
val versionNameInput = providers.gradleProperty("kurastorage.versionName").orElse("0.1.0")
val versionCodeInput = providers.gradleProperty("kurastorage.versionCode").orElse("1")
val generatedReleaseResources = layout.buildDirectory.dir("generated/releaseRootCa/res")
val releaseBuildRequested =
    gradle.startParameter.taskNames.any {
        it.substringAfterLast(':').matches(Regex("(?i)(assemble|bundle).*release"))
    }

if (releaseBuildRequested && !rootCaCertificate.isPresent) {
    throw GradleException(
        "Release builds require -Pkurastorage.rootCaCertificate=/path/to/public-root-ca.pem",
    )
}

val generateReleaseRootCa by tasks.registering(Sync::class) {
    into(generatedReleaseResources)
    from(rootCaCertificate) {
        into("raw")
        rename { "kurastorage_root_ca.pem" }
    }
    from("src/release/templates/network_security_config.xml.template") {
        into("xml")
        rename { "network_security_config.xml" }
        filter<ReplaceTokens>(
            "tokens" to mapOf("API_HOSTNAME" to apiHostname.get()),
        )
    }
}

android {
    namespace = "com.kurastorage.app"
    buildFeatures.buildConfig = true
    defaultConfig {
        applicationId = "com.kurastorage.app"
        versionCode = versionCodeInput.get().toInt()
        versionName = versionNameInput.get()
        buildConfigField("String", "API_HOSTNAME", "\"${apiHostname.get()}\"")
        buildConfigField("String", "LAN_API_ADDRESS", "\"${lanApiAddress.get()}\"")
        buildConfigField("String", "ZEROTIER_API_ADDRESS", "\"${zerotierApiAddress.get()}\"")
    }
    buildTypes.getByName("debug") {
        applicationIdSuffix = ".debug"
        versionNameSuffix = "-debug"
    }
    if (releaseBuildRequested) {
        val keystorePath = providers.environmentVariable("KURASTORAGE_RELEASE_KEYSTORE").get()
        val keyAliasInput = providers.environmentVariable("KURASTORAGE_RELEASE_KEY_ALIAS").get()
        val storePasswordFile =
            providers.environmentVariable("KURASTORAGE_RELEASE_STORE_PASSWORD_FILE").get()
        val keyPasswordFile =
            providers.environmentVariable("KURASTORAGE_RELEASE_KEY_PASSWORD_FILE").get()
        signingConfigs.create("release") {
            storeFile = file(keystorePath)
            keyAlias = keyAliasInput
            storePassword = file(storePasswordFile).readText().trimEnd()
            keyPassword = file(keyPasswordFile).readText().trimEnd()
            enableV1Signing = true
            enableV2Signing = true
            enableV3Signing = true
        }
        buildTypes.getByName("release").signingConfig = signingConfigs.getByName("release")
    }
    sourceSets["release"].res.srcDir(generatedReleaseResources)
}

tasks.matching { it.name == "preReleaseBuild" }.configureEach {
    dependsOn(generateReleaseRootCa)
}

dependencies {
    implementation(project(":core-model"))
    implementation(project(":core-network"))
    implementation(project(":core-data"))
    implementation(project(":core-security"))
    implementation(project(":core-ui"))
    implementation(project(":feature-connection"))
    implementation(project(":feature-auth"))
    implementation(project(":feature-files"))
    implementation(project(":feature-sharing"))
    implementation(project(":feature-search"))
    implementation(project(":feature-media"))
    implementation(project(":feature-settings"))
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.okhttp)
    implementation(libs.coil.core)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.espresso.core)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
