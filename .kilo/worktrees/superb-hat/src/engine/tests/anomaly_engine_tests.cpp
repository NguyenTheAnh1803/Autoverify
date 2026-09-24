#include "anomaly_engine.hpp"

#include <cassert>

int main() {
    const autoverify::engine::RobustThresholdAnalyzer analyzer;
    const std::vector<autoverify::engine::FeatureRecord> records{
        {"normal", {{"mismatchRatio", 5.0}}},
        {"abnormal", {{"mismatchRatio", 95.0}}}
    };

    const auto results = analyzer.analyze(records);
    assert(results.size() == 2);
    assert(results[0].classification == "Normal");
    assert(results[1].classification == "AnomalyDetected");

    return 0;
}
