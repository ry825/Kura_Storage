package com.kurastorage.app

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider

inline fun <reified T : ViewModel> simpleViewModelFactory(crossinline create: () -> T): ViewModelProvider.Factory =
    object : ViewModelProvider.Factory {
        override fun <ModelType : ViewModel> create(modelClass: Class<ModelType>): ModelType {
            require(modelClass.isAssignableFrom(T::class.java))
            @Suppress("UNCHECKED_CAST")
            return create() as ModelType
        }
    }
