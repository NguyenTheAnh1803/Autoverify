#pragma once

#include <string>
#include <unordered_map>
#include <vector>

namespace autoverify::engine {

struct FeatureRecord {
    std::string entity_id;
    std::unordered_map<std::string, double> numeric_features;
};

struct AnomalyResult {
    std::string entity_id;
    double score;
    std::string classification;
    std::vector<std::string> contributing_features;
    std::string evidence;
};

class RobustThresholdAnalyzer {
public:
    std::vector<AnomalyResult> analyze(const std::vector<FeatureRecord>& records) const;
};

}  // namespace autoverify::engine
