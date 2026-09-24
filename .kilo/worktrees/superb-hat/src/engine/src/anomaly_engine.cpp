#include "anomaly_engine.hpp"

#include <algorithm>
#include <cmath>
#include <numeric>
#include <sstream>

namespace autoverify::engine {

std::vector<AnomalyResult> RobustThresholdAnalyzer::analyze(const std::vector<FeatureRecord>& records) const {
    std::vector<AnomalyResult> results;
    results.reserve(records.size());

    for (const auto& record : records) {
        double total = 0.0;
        std::vector<std::string> contributors;

        for (const auto& [name, value] : record.numeric_features) {
            const auto normalized = std::min(std::abs(value) / 100.0, 1.0);
            total += normalized;
            if (normalized >= 0.75) {
                contributors.push_back(name);
            }
        }

        const auto feature_count = std::max<std::size_t>(record.numeric_features.size(), 1);
        const auto score = std::min(total / static_cast<double>(feature_count), 1.0);
        const auto classification = score >= 0.75 ? "AnomalyDetected" : score >= 0.45 ? "Warning" : "Normal";

        std::ostringstream evidence;
        evidence << "MVP robust threshold score=" << score << ", numericFeatureCount=" << record.numeric_features.size();

        results.push_back(AnomalyResult{
            record.entity_id,
            score,
            classification,
            contributors,
            evidence.str()
        });
    }

    return results;
}

}  // namespace autoverify::engine
