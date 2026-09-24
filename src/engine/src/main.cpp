#include "anomaly_engine.hpp"

#include <iostream>

int main() {
    const autoverify::engine::RobustThresholdAnalyzer analyzer;
    const std::vector<autoverify::engine::FeatureRecord> records{
        {"AUTO-000001", {{"missingRatio", 12.0}, {"mismatchRatio", 8.0}}},
        {"AUTO-000002", {{"missingRatio", 91.0}, {"mismatchRatio", 82.0}}}
    };

    const auto results = analyzer.analyze(records);
    for (const auto& result : results) {
        std::cout << result.entity_id << " " << result.classification << " " << result.score << '\n';
    }

    return 0;
}
