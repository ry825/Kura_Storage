plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
}

android.namespace = "com.kurastorage.feature.files"

dependencies {
    implementation(project(":core-model"))
    implementation(project(":core-data"))
    implementation(project(":core-ui"))
}
