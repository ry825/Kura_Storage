plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
}

android.namespace = "com.kurastorage.core.network"

android.sourceSets["test"].resources.srcDir("../../../contracts/fixtures")

dependencies {
    implementation(project(":core-model"))
    implementation(project(":core-security"))
}
