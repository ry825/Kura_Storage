plugins {
    id("kurastorage.android.application")
    id("kurastorage.android.compose")
    id("kurastorage.android.test")
}

val apiHostname = providers.gradleProperty("kurastorage.apiHostname").orElse("api.kurastorage.example")
val lanApiAddress = providers.gradleProperty("kurastorage.lanApiAddress").orElse("192.0.2.10")
val zerotierApiAddress = providers.gradleProperty("kurastorage.zerotierApiAddress").orElse("198.51.100.10")
val rootCaCertificate = providers.gradleProperty("kurastorage.rootCaCertificate")
val generatedReleaseResources = layout.buildDirectory.dir("generated/releaseRootCa/res")

val generateReleaseRootCa by tasks.registering(Copy::class) {
    doFirst {
        require(rootCaCertificate.isPresent) {
            "Release builds require -Pkurastorage.rootCaCertificate=/path/to/public-root-ca.pem"
        }
    }
    from(rootCaCertificate)
    into(generatedReleaseResources.map { it.dir("raw") })
    rename { "kurastorage_root_ca.pem" }
    doLast {
        val xmlDirectory = generatedReleaseResources.get().dir("xml").asFile
        xmlDirectory.mkdirs()
        xmlDirectory.resolve("network_security_config.xml").writeText(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <network-security-config>
                <base-config cleartextTrafficPermitted="false" />
                <domain-config cleartextTrafficPermitted="false">
                    <domain includeSubdomains="false">${apiHostname.get()}</domain>
                    <trust-anchors>
                        <certificates src="@raw/kurastorage_root_ca" />
                    </trust-anchors>
                </domain-config>
            </network-security-config>
            """.trimIndent(),
        )
    }
}

android {
    namespace = "com.kurastorage.app"
    buildFeatures.buildConfig = true
    defaultConfig {
        applicationId = "com.kurastorage.app"
        versionCode = 1
        versionName = "0.1.0"
        buildConfigField("String", "API_HOSTNAME", "\"${apiHostname.get()}\"")
        buildConfigField("String", "LAN_API_ADDRESS", "\"${lanApiAddress.get()}\"")
        buildConfigField("String", "ZEROTIER_API_ADDRESS", "\"${zerotierApiAddress.get()}\"")
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
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.okhttp)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.espresso.core)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
