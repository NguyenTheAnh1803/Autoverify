cmake_minimum_required(VERSION 3.24)

project(AutoVerifyEngine VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(autoverify_engine_core
    src/anomaly_engine.cpp
)

target_include_directories(autoverify_engine_core PUBLIC include)

add_executable(autoverify-engine
    src/main.cpp
)

target_link_libraries(autoverify-engine PRIVATE autoverify_engine_core)

enable_testing()
add_executable(autoverify-engine-tests
    tests/anomaly_engine_tests.cpp
)
target_link_libraries(autoverify-engine-tests PRIVATE autoverify_engine_core)
add_test(NAME autoverify-engine-tests COMMAND autoverify-engine-tests)
