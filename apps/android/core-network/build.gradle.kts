plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
    alias(libs.plugins.kotlin.serialization)
}

android.namespace = "com.kurastorage.core.network"

android.sourceSets["test"].resources.srcDir("../../../contracts/fixtures")

dependencies {
    implementation(project(":core-model"))
    implementation(project(":core-security"))
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.okhttp)
    implementation(libs.retrofit)
    implementation(libs.retrofit.kotlinx.serialization)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.okhttp.mockwebserver)
}
