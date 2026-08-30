plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
}

android.namespace = "com.kurastorage.core.data"

dependencies {
    implementation(project(":core-model"))
    implementation(project(":core-network"))
    implementation(project(":core-security"))
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.coil.core)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.okhttp)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.okhttp.mockwebserver)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
}
