import org.gradle.api.Plugin
import org.gradle.api.Project
import org.gradle.kotlin.dsl.configure

class KuraStorageAndroidTestPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        target.dependencies.add("testImplementation", "junit:junit:4.13.2")
        target.pluginManager.withPlugin("com.android.application") {
            target.extensions.configure<com.android.build.api.dsl.ApplicationExtension> {
                defaultConfig.testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
            }
        }
        target.pluginManager.withPlugin("com.android.library") {
            target.extensions.configure<com.android.build.api.dsl.LibraryExtension> {
                defaultConfig.testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
            }
        }
    }
}
