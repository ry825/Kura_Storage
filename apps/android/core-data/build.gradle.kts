plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
}

android.namespace = "com.kurastorage.core.data"

dependencies {
    implementation(project(":core-model"))
    implementation(project(":core-network"))
    implementation(project(":core-security"))
}
