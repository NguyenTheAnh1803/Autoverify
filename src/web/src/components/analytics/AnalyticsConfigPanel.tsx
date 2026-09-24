import React, { useState, useEffect } from 'react'
import { classifierRulesApi } from '../../api'
import type {
  FeatureSummary,
  AnalysisReadiness,
  AnalyticsJobType,
  CreateAnalyticsJobRequest,
  AnalyticsScopeFilter,
  ClassifierRuleDto,
} from '../../types'

export type AnalyticsConfigPanelProps = {
  family: AnalyticsJobType | string
  features: FeatureSummary[]
  readiness?: AnalysisReadiness
  detectedRuleVersions?: string[] | null
  scopeWarnings?: string[] | null
  scope?: AnalyticsScopeFilter
  resolvedRule?: ClassifierRuleDto | null
  onSubmitJob: (request: CreateAnalyticsJobRequest) => void
  submitting: boolean
}

export const AnalyticsConfigPanel: React.FC<AnalyticsConfigPanelProps> = ({
  family,
  features,
  readiness,
  detectedRuleVersions,
  scopeWarnings,
  scope,
  resolvedRule,
  onSubmitJob,
  submitting,
}) => {
  const normFamily = family.toUpperCase()
  const featureNames = features.map((f) => f.name)
  const defaultFeature = readiness?.recommendedFeatures?.[0] || featureNames[0] || ''

  // Common options
  const [seed, setSeed] = useState<number>(42)
  const [missingValuePolicy, setMissingValuePolicy] = useState<string>('CompleteCase')
  const [scalingPolicy, setScalingPolicy] = useState<string>('Standard')

  // SPC state & safeguards
  const [spcFeature, setSpcFeature] = useState(defaultFeature)
  const [spcChartType, setSpcChartType] = useState<'IMR' | 'XbarR' | 'XbarS'>('IMR')
  const [spcSubgroupIdField, setSpcSubgroupIdField] = useState('')
  const [spcSubgroupSize, setSpcSubgroupSize] = useState<number>(5)
  const [spcUsl, setSpcUsl] = useState<string>('')
  const [spcLsl, setSpcLsl] = useState<string>('')
  const [spcTarget, setSpcTarget] = useState<string>('')
  const [allowRuleVersionMixing, setAllowRuleVersionMixing] = useState<boolean>(false)

  // EWMA state
  const [ewmaFeature, setEwmaFeature] = useState(defaultFeature)
  const [ewmaLambda, setEwmaLambda] = useState<number>(0.2)
  const [ewmaL, setEwmaL] = useState<number>(3.0)
  const [ewmaTarget, setEwmaTarget] = useState<string>('')
  const [ewmaSigma, setEwmaSigma] = useState<string>('')

  // CUSUM state
  const [cusumFeature, setCusumFeature] = useState(defaultFeature)
  const [cusumK, setCusumK] = useState<number>(0.5)
  const [cusumH, setCusumH] = useState<number>(4.0)
  const [cusumTarget, setCusumTarget] = useState<string>('')
  const [cusumSigma, setCusumSigma] = useState<string>('')

  // PCA state
  const [pcaFeatures, setPcaFeatures] = useState<string[]>(
    readiness?.recommendedFeatures && readiness.recommendedFeatures.length >= 2
      ? readiness.recommendedFeatures
      : featureNames.slice(0, 4)
  )
  const [pcaRetained, setPcaRetained] = useState<number>(3)
  const [pcaConfidence, setPcaConfidence] = useState<number>(0.95)

  // MSA state
  const [msaFeature, setMsaFeature] = useState(defaultFeature)
  const [msaPartField, setMsaPartField] = useState<string>('ComponentName')
  const [msaOperatorField, setMsaOperatorField] = useState<string>('EqpName')
  const [msaSubgroupIdField, setMsaSubgroupIdField] = useState<string>('')
  const [msaAlpha, setMsaAlpha] = useState<number>(0.05)

  // DOE state
  const [doeResponse, setDoeResponse] = useState(defaultFeature)
  const [doeFactors, setDoeFactors] = useState<string[]>(
    featureNames.filter((f) => f !== defaultFeature).slice(0, 3)
  )
  const [doeAlpha, setDoeAlpha] = useState<number>(0.05)

  // Feature Relationship state
  const [relFeatures, setRelFeatures] = useState<string[]>(
    readiness?.recommendedFeatures && readiness.recommendedFeatures.length >= 2
      ? readiness.recommendedFeatures
      : featureNames.slice(0, 6)
  )
  const [relMaxScatter, setRelMaxScatter] = useState<number>(1000)

  // K-means state
  const [kmeansFeatures, setKmeansFeatures] = useState<string[]>(
    readiness?.recommendedFeatures && readiness.recommendedFeatures.length >= 2
      ? readiness.recommendedFeatures
      : featureNames.slice(0, 4)
  )
  const [kmeansK, setKmeansK] = useState<number>(3)

  // GMM state
  const [gmmFeatures, setGmmFeatures] = useState<string[]>(
    readiness?.recommendedFeatures && readiness.recommendedFeatures.length >= 2
      ? readiness.recommendedFeatures
      : featureNames.slice(0, 4)
  )
  const [gmmComponents, setGmmComponents] = useState<number>(3)
  const [gmmCovariance, setGmmCovariance] = useState<'diagonal' | 'full'>('diagonal')

  // Isolation Forest state
  const [ifFeatures, setIfFeatures] = useState<string[]>(
    readiness?.recommendedFeatures && readiness.recommendedFeatures.length >= 2
      ? readiness.recommendedFeatures
      : featureNames.slice(0, 4)
  )
  const [ifTrees, setIfTrees] = useState<number>(100)
  const [ifSubsample, setIfSubsample] = useState<number>(256)
  const [ifContamination, setIfContamination] = useState<number>(0.1)

  // Script Condition state
  const [scriptText, setScriptText] = useState<string>('')
  const [maxEvalRows, setMaxEvalRows] = useState<number>(500)
  const [savingRuleScript, setSavingRuleScript] = useState<boolean>(false)
  const [saveScriptMessage, setSaveScriptMessage] = useState<string | null>(null)

  const DEFAULT_AOI_DIC_CRACK_SCRIPT = `if( G010_VpID != 1):
\treturn False

if( G008_LightID != 1 ):
\treturn False
\t
if( G009_RoiID != 1 ):
\treturn False
\t
if(G131_Real_DicToLeft>400 and G132_Real_DicToRigt>400 and G133_Real_DicToTop>400 and G134_Real_DicToBot>400 and G063_OrgGrayMin> 120):
\treturn False

IP_Cond = V003_VP_IP == 200131 or V003_VP_IP == 200231
if(V003_VP_IP == 1):
\tIP_Cond = 1

VP_Info1 = (G008_LightID ==1) and (G009_RoiID == 1)

Cond1 = ( G020_PxlArea > 90 and (G017_PxlWidth > 20 or G018_PxlHeight > 20))
Cond2 = ( G038_PrcGrayMin <  45 )
#Improve DIC CRACK line  240807
AVG_Line = G018_PxlHeight /G017_PxlWidth
if(G018_PxlHeight <  10):
\tCond2 = (G038_PrcGrayMin < 220)
if(Cond2 ==1 and AVG_Line < 0.8):
\tCond1 = (G020_PxlArea > 20 )
\t
if(G017_PxlWidth > 80):
\tCond2 = (G038_PrcGrayMin <  150)

DL_Cond = MLClass01 > 90

return IP_Cond and VP_Info1 and Cond1 and Cond2 and DL_Cond`

  const DEFAULT_AOI_LIGHT_FAIL_SCRIPT = `#MCL Block1
MCL_Position1 = (G010_VpID==1) and (G008_LightID ==1) and (G009_RoiID == 24 or G009_RoiID == 25)
MCL_Light1 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
MCL_Block1 = MCL_Position1 and MCL_Light1

#MCL Block2
MCL_Position2 = (G010_VpID==1) and (G008_LightID ==2) and (G009_RoiID == 3 or G009_RoiID == 4)
MCL_Light2 = (G062_OrgGrayMax < 30 and G064_OrgGrayStdDev < 5)
MCL_Block2 = MCL_Position2 and MCL_Light2

#MCL Block4
MCL_Position4 = (G010_VpID==1) and (G008_LightID ==4) and (G009_RoiID == 8 or G009_RoiID == 9)
MCL_Light4 = (G062_OrgGrayMax < 30 and G064_OrgGrayStdDev < 5)
MCL_Block4 = MCL_Position4 and MCL_Light4

#SR_Block1
SR_Position1 = (G010_VpID==2) and (G008_LightID ==1) and (G009_RoiID == 2 or G009_RoiID == 3)
SR_Light1 = (G062_OrgGrayMax < 50 and G064_OrgGrayStdDev < 5)
SR_Block1 = SR_Position1 and SR_Light1

#SR_Block2
SR_Position2 = (G010_VpID==2) and (G008_LightID ==2) and (G009_RoiID == 5 or G009_RoiID == 6)
SR_Light2 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
SR_Block2 = SR_Position2 and SR_Light2

#SR_Block3
SR_Position3 = (G010_VpID==2) and (G008_LightID ==3) and (G009_RoiID == 2 or G009_RoiID == 3)
SR_Light3 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
SR_Block3 = SR_Position3 and SR_Light3

return MCL_Block1 or MCL_Block2 or MCL_Block4 or SR_Block1 or SR_Block2 or SR_Block3`

  const handleImportRulesJsonText = async (rawText: string) => {
    try {
      const parsed = JSON.parse(rawText) as { Rules?: Array<{ Priority: number; Name: string; GUID?: string; DefectTypeID?: string; Script?: string }> }
      const rules = parsed.Rules || (Array.isArray(parsed) ? parsed : null)
      if (rules && rules.length > 0) {
        setSavingRuleScript(true)
        const mapped = rules.map((r) => ({
          priority: r.Priority ?? 0,
          name: r.Name,
          guid: r.GUID,
          defectTypeId: r.DefectTypeID,
          script: r.Script,
        }))
        await classifierRulesApi.importAoiRulesJson({
          rules: mapped,
          lineCode: scope?.lineCode,
          machineCode: scope?.machineCode,
        })
        setSaveScriptMessage(`✓ Successfully imported ${mapped.length} Rules from JSON into Database!`)
        // Pick script of first rule or matching rule
        const first = mapped[0]
        if (first?.script) {
          setScriptText(first.script)
        }
        setTimeout(() => setSaveScriptMessage(null), 6000)
      } else {
        setSaveScriptMessage('Could not find "Rules" array in JSON.')
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err)
      setSaveScriptMessage(`JSON Parse Error: ${msg}`)
    } finally {
      setSavingRuleScript(false)
    }
  }

  // Auto-fill script from resolved rule if textarea is empty
  useEffect(() => {
    if (normFamily === 'SCRIPT_CONDITION' && resolvedRule?.scriptContent && !scriptText.trim()) {
      setScriptText(resolvedRule.scriptContent)
    }
  }, [normFamily, resolvedRule?.scriptContent])

  const handleSaveScriptToRule = async () => {
    if (!resolvedRule?.id) return
    setSavingRuleScript(true)
    setSaveScriptMessage(null)
    try {
      await classifierRulesApi.updateRule(resolvedRule.id, {
        scriptContent: scriptText,
      })
      setSaveScriptMessage(`✓ Saved script into Rule ${resolvedRule.ruleCode} (v${resolvedRule.version}) successfully!`)
      setTimeout(() => setSaveScriptMessage(null), 5000)
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err)
      setSaveScriptMessage(`Error saving script: ${msg}`)
    } finally {
      setSavingRuleScript(false)
    }
  }

  // Keep defaults updated if recommendations change
  useEffect(() => {
    if (readiness?.recommendedFeatures && readiness.recommendedFeatures.length > 0) {
      const rec = readiness.recommendedFeatures[0]
      if (normFamily === 'SPC' && !spcFeature) setSpcFeature(rec)
      if (normFamily === 'EWMA' && !ewmaFeature) setEwmaFeature(rec)
      if (normFamily === 'CUSUM' && !cusumFeature) setCusumFeature(rec)
      if (normFamily === 'MSA' && !msaFeature) setMsaFeature(rec)
      if (normFamily === 'DOE' && !doeResponse) setDoeResponse(rec)
    }
  }, [normFamily, readiness, spcFeature, ewmaFeature, cusumFeature, msaFeature, doeResponse])

  // Multi-version SPC safeguard:
  const hasMultipleVersions = (detectedRuleVersions?.length ?? 0) > 1
  const isSpcBlockedByVersions = normFamily === 'SPC' && hasMultipleVersions && !allowRuleVersionMixing

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()

    let req: CreateAnalyticsJobRequest

    switch (normFamily) {
      case 'SPC': {
        const params: Record<string, unknown> = {
          ChartType: spcChartType,
        }
        if (spcChartType !== 'IMR') {
          params.SubgroupSize = spcSubgroupSize
        }
        if (spcUsl.trim() !== '') params.USL = parseFloat(spcUsl)
        if (spcLsl.trim() !== '') params.LSL = parseFloat(spcLsl)
        if (spcTarget.trim() !== '') params.Target = parseFloat(spcTarget)

        req = {
          analysisType: 'SPC',
          featureName: spcFeature,
          subgroupIdField: spcSubgroupIdField.trim() || undefined,
          parameters: params,
          allowRuleVersionMixing,
        }
        break
      }
      case 'EWMA': {
        const params: Record<string, unknown> = {
          Lambda: ewmaLambda,
          L: ewmaL,
        }
        if (ewmaTarget.trim() !== '') params.Target = parseFloat(ewmaTarget)
        if (ewmaSigma.trim() !== '') params.Sigma = parseFloat(ewmaSigma)

        req = {
          analysisType: 'EWMA',
          featureName: ewmaFeature,
          parameters: params,
        }
        break
      }
      case 'CUSUM': {
        const params: Record<string, unknown> = {
          K: cusumK,
          H: cusumH,
        }
        if (cusumTarget.trim() !== '') params.Target = parseFloat(cusumTarget)
        if (cusumSigma.trim() !== '') params.Sigma = parseFloat(cusumSigma)

        req = {
          analysisType: 'CUSUM',
          featureName: cusumFeature,
          parameters: params,
        }
        break
      }
      case 'PCA': {
        req = {
          analysisType: 'PCA',
          featureNames: pcaFeatures,
          parameters: {
            RetainedComponents: pcaRetained,
            ConfidenceLevel: pcaConfidence,
          },
        }
        break
      }
      case 'MSA': {
        req = {
          analysisType: 'MSA',
          featureName: msaFeature,
          partField: msaPartField,
          operatorField: msaOperatorField,
          subgroupIdField: msaSubgroupIdField.trim() || undefined,
          parameters: {
            Alpha: msaAlpha,
          },
        }
        break
      }
      case 'DOE': {
        req = {
          analysisType: 'DOE',
          responseFeature: doeResponse,
          factorFeatures: doeFactors,
          parameters: {
            Alpha: doeAlpha,
          },
        }
        break
      }
      case 'FEATURE_RELATIONSHIP': {
        req = {
          analysisType: 'FEATURE_RELATIONSHIP',
          featureNames: relFeatures,
          maxScatterPoints: relMaxScatter,
          missingValuePolicy,
        }
        break
      }
      case 'KMEANS': {
        req = {
          analysisType: 'KMEANS',
          featureNames: kmeansFeatures,
          k: kmeansK,
          seed,
          scalingPolicy,
          missingValuePolicy,
        }
        break
      }
      case 'GMM': {
        req = {
          analysisType: 'GMM',
          featureNames: gmmFeatures,
          componentsCount: gmmComponents,
          covarianceType: gmmCovariance,
          seed,
          scalingPolicy,
          missingValuePolicy,
        }
        break
      }
      case 'ISOLATION_FOREST': {
        req = {
          analysisType: 'ISOLATION_FOREST',
          featureNames: ifFeatures,
          treesCount: ifTrees,
          subsampleSize: ifSubsample,
          contamination: ifContamination,
          seed,
          missingValuePolicy,
        }
        break
      }
      case 'SCRIPT_CONDITION': {
        req = {
          analysisType: 'SCRIPT_CONDITION',
          scriptText: scriptText,
          maxEvaluationRows: maxEvalRows,
        }
        break
      }
      default: {
        req = {
          analysisType: family,
          featureName: defaultFeature,
        }
        break
      }
    }

    // Attach scope filters if provided
    if (scope) {
      req.scopeFilter = scope
      req.productKey = scope.productKey
      req.lineCode = scope.lineCode
      req.machineCode = scope.machineCode
      req.stationCode = scope.stationCode
      req.classifierRuleCode = scope.classifierRuleCode
      req.classifierRuleVersion = scope.classifierRuleVersion
      req.defectFamily = scope.defectFamily
      req.defectType = scope.defectType
      req.timeFromUtc = scope.timeFromUtc
      req.timeToUtc = scope.timeToUtc
    }

    onSubmitJob(req)
  }

  const isBlocked = (readiness ? !readiness.isReady : false) || isSpcBlockedByVersions

  return (
    <div className="analytics-config-card">
      <div className="config-header">
        <h4>Configuration & Parameters: {normFamily}</h4>
        {isBlocked && (
          <span className="badge-danger font-mono text-xs">
            BLOCKED BY PRECONDITIONS
          </span>
        )}
      </div>

      {/* Scope Warnings if returned */}
      {scopeWarnings && scopeWarnings.length > 0 && (
        <div className="analytics-alert-banner warning text-xs my-2">
          <strong>Scope Notice:</strong>
          <ul className="mt-1 list-disc pl-4">
            {scopeWarnings.map((w, i) => (
              <li key={i}>{w}</li>
            ))}
          </ul>
        </div>
      )}

      {/* SPC Multi-Rule Version Warning / Safeguard */}
      {normFamily === 'SPC' && hasMultipleVersions && (
        <div className="analytics-alert-banner warning text-xs my-2">
          <strong>Mixed Classifier-Rule Versions Detected:</strong>
          <p className="mt-1">
            Records in this session originate from multiple rule versions ({detectedRuleVersions?.join(', ')}). Mixing differing classifier rule versions into a single SPC baseline creates artificial variance steps and invalidates control limits.
          </p>
          <label className="checkbox-item font-semibold mt-2 inline-flex items-center gap-2">
            <input
              type="checkbox"
              checked={allowRuleVersionMixing}
              onChange={(e) => setAllowRuleVersionMixing(e.target.checked)}
            />
            Explicitly permit mixing multiple rule versions in this SPC job
          </label>
        </div>
      )}

      {/* Precondition Blockers */}
      {readiness && !readiness.isReady && readiness.blockers.length > 0 && (
        <div className="analytics-alert-banner error text-sm my-2">
          <strong>Cannot execute {normFamily}:</strong>
          <ul className="mt-1 list-disc pl-4">
            {readiness.blockers.map((b, i) => (
              <li key={i}>{b}</li>
            ))}
          </ul>
        </div>
      )}

      <form onSubmit={handleSubmit} className="config-form">
        {/* SPC FORM */}
        {normFamily === 'SPC' && (
          <div className="form-grid">
            <div className="form-group">
              <label>Target Defect Feature *</label>
              <select
                value={spcFeature}
                onChange={(e) => setSpcFeature(e.target.value)}
                required
              >
                {featureNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label>Chart Type</label>
              <select
                value={spcChartType}
                onChange={(e) => setSpcChartType(e.target.value as 'IMR' | 'XbarR' | 'XbarS')}
              >
                <option value="IMR">I-MR (Individual & Moving Range)</option>
                <option value="XbarR">Xbar-R (Average & Range)</option>
                <option value="XbarS">Xbar-S (Average & Standard Deviation)</option>
              </select>
            </div>

            {spcChartType !== 'IMR' && (
              <div className="form-group">
                <label>Subgroup Size (n)</label>
                <input
                  type="number"
                  min={2}
                  max={25}
                  value={spcSubgroupSize}
                  onChange={(e) => setSpcSubgroupSize(parseInt(e.target.value, 10) || 5)}
                />
              </div>
            )}

            <div className="form-group">
              <label>Subgroup ID Column (optional)</label>
              <input
                type="text"
                placeholder="e.g. LotNumber, BatchId, or Empty"
                value={spcSubgroupIdField}
                onChange={(e) => setSpcSubgroupIdField(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label>LSL (Lower Spec Limit)</label>
              <input
                type="number"
                step="any"
                placeholder="Optional"
                value={spcLsl}
                onChange={(e) => setSpcLsl(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label>USL (Upper Spec Limit)</label>
              <input
                type="number"
                step="any"
                placeholder="Optional"
                value={spcUsl}
                onChange={(e) => setSpcUsl(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label>Target Nominal</label>
              <input
                type="number"
                step="any"
                placeholder="Optional"
                value={spcTarget}
                onChange={(e) => setSpcTarget(e.target.value)}
              />
            </div>
          </div>
        )}

        {/* EWMA FORM */}
        {normFamily === 'EWMA' && (
          <div className="form-grid">
            <div className="form-group">
              <label>Target Defect Feature *</label>
              <select
                value={ewmaFeature}
                onChange={(e) => setEwmaFeature(e.target.value)}
                required
              >
                {featureNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label>Smoothing Parameter Lambda (λ: 0.05 - 0.40)</label>
              <input
                type="number"
                step="0.05"
                min="0.01"
                max="1.0"
                value={ewmaLambda}
                onChange={(e) => setEwmaLambda(parseFloat(e.target.value) || 0.2)}
              />
            </div>

            <div className="form-group">
              <label>Control Limit Multiplier (L: 2.7 - 3.0)</label>
              <input
                type="number"
                step="0.1"
                min="1.0"
                max="5.0"
                value={ewmaL}
                onChange={(e) => setEwmaL(parseFloat(e.target.value) || 3.0)}
              />
            </div>

            <div className="form-group">
              <label>Known Process Target (optional)</label>
              <input
                type="number"
                step="any"
                placeholder="Defaults to sample mean"
                value={ewmaTarget}
                onChange={(e) => setEwmaTarget(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label>Known Sigma (optional)</label>
              <input
                type="number"
                step="any"
                placeholder="Defaults to sample std dev"
                value={ewmaSigma}
                onChange={(e) => setEwmaSigma(e.target.value)}
              />
            </div>
          </div>
        )}

        {/* CUSUM FORM */}
        {normFamily === 'CUSUM' && (
          <div className="form-grid">
            <div className="form-group">
              <label>Target Defect Feature *</label>
              <select
                value={cusumFeature}
                onChange={(e) => setCusumFeature(e.target.value)}
                required
              >
                {featureNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label>Slack Allowance (k: typically 0.5)</label>
              <input
                type="number"
                step="0.1"
                min="0.1"
                max="3.0"
                value={cusumK}
                onChange={(e) => setCusumK(parseFloat(e.target.value) || 0.5)}
              />
            </div>

            <div className="form-group">
              <label>Decision Interval Threshold (h: typically 4.0 - 5.0)</label>
              <input
                type="number"
                step="0.5"
                min="1.0"
                max="10.0"
                value={cusumH}
                onChange={(e) => setCusumH(parseFloat(e.target.value) || 4.0)}
              />
            </div>

            <div className="form-group">
              <label>Target Nominal (optional)</label>
              <input
                type="number"
                step="any"
                placeholder="Defaults to sample mean"
                value={cusumTarget}
                onChange={(e) => setCusumTarget(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label>Known Sigma (optional)</label>
              <input
                type="number"
                step="any"
                placeholder="Defaults to sample std dev"
                value={cusumSigma}
                onChange={(e) => setCusumSigma(e.target.value)}
              />
            </div>
          </div>
        )}

        {/* PCA FORM */}
        {normFamily === 'PCA' && (
          <div className="form-vertical">
            <div className="form-group">
              <label>Select Features for Dimensionality Reduction (min 2) *</label>
              <div className="feature-checkbox-grid">
                {featureNames.map((name) => {
                  const checked = pcaFeatures.includes(name)
                  return (
                    <label key={name} className="checkbox-item font-mono text-xs">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setPcaFeatures([...pcaFeatures, name])
                          } else {
                            setPcaFeatures(pcaFeatures.filter((f) => f !== name))
                          }
                        }}
                      />
                      {name}
                    </label>
                  )
                })}
              </div>
            </div>

            <div className="form-grid mt-2">
              <div className="form-group">
                <label>Retained Components (Scree limit)</label>
                <input
                  type="number"
                  min={2}
                  max={Math.min(10, pcaFeatures.length || 2)}
                  value={pcaRetained}
                  onChange={(e) => setPcaRetained(parseInt(e.target.value, 10) || 2)}
                />
              </div>

              <div className="form-group">
                <label>Confidence Level (Hotelling T² & SPE/Q)</label>
                <select
                  value={pcaConfidence}
                  onChange={(e) => setPcaConfidence(parseFloat(e.target.value))}
                >
                  <option value={0.90}>90% (α = 0.10)</option>
                  <option value={0.95}>95% (α = 0.05)</option>
                  <option value={0.99}>99% (α = 0.01)</option>
                </select>
              </div>
            </div>
          </div>
        )}

        {/* MSA FORM */}
        {normFamily === 'MSA' && (
          <div className="form-grid">
            <div className="form-group">
              <label>Continuous Measurement Feature *</label>
              <select
                value={msaFeature}
                onChange={(e) => setMsaFeature(e.target.value)}
                required
              >
                {featureNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label>Part / Specimen Identifier Field *</label>
              <select
                value={msaPartField}
                onChange={(e) => setMsaPartField(e.target.value)}
              >
                <option value="ComponentName">ComponentName</option>
                <option value="KeyId">KeyId</option>
                <option value="EmNo">EmNo</option>
              </select>
            </div>

            <div className="form-group">
              <label>Operator / Equipment Field *</label>
              <select
                value={msaOperatorField}
                onChange={(e) => setMsaOperatorField(e.target.value)}
              >
                <option value="EqpName">EqpName (Machine/Tool)</option>
                <option value="EmNo">EmNo (Employee / Operator)</option>
                <option value="KeyId">KeyId</option>
              </select>
            </div>

            <div className="form-group">
              <label>Subgroup / Repeat Field (optional)</label>
              <input
                type="text"
                placeholder="Optional trial/replicate field"
                value={msaSubgroupIdField}
                onChange={(e) => setMsaSubgroupIdField(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label>Significance Alpha (α for Pooling)</label>
              <select
                value={msaAlpha}
                onChange={(e) => setMsaAlpha(parseFloat(e.target.value))}
              >
                <option value={0.05}>0.05 (Default)</option>
                <option value={0.25}>0.25 (Standard AIAG Pooling Alpha)</option>
              </select>
            </div>
          </div>
        )}

        {/* DOE FORM */}
        {normFamily === 'DOE' && (
          <div className="form-vertical">
            <div className="form-group">
              <label>Continuous Response Variable (Y) *</label>
              <select
                value={doeResponse}
                onChange={(e) => setDoeResponse(e.target.value)}
                required
              >
                {featureNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group mt-2">
              <label>Experimental Factor Features (X1, X2, ...) (2 - 5 factors) *</label>
              <div className="feature-checkbox-grid">
                {featureNames
                  .filter((f) => f !== doeResponse)
                  .map((fName) => {
                    const checked = doeFactors.includes(fName)
                    return (
                      <label key={fName} className="checkbox-item font-mono text-xs">
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={(e) => {
                            if (e.target.checked) {
                              setDoeFactors([...doeFactors, fName])
                            } else {
                              setDoeFactors(doeFactors.filter((f) => f !== fName))
                            }
                          }}
                        />
                        {fName}
                      </label>
                    )
                  })}
              </div>
            </div>

            <div className="form-group mt-2">
              <label>Significance Alpha (α)</label>
              <select
                value={doeAlpha}
                onChange={(e) => setDoeAlpha(parseFloat(e.target.value))}
              >
                <option value={0.01}>0.01 (Strict 99%)</option>
                <option value={0.05}>0.05 (Standard 95%)</option>
                <option value={0.10}>0.10 (Exploratory 90%)</option>
              </select>
            </div>
          </div>
        )}

        {/* FEATURE RELATIONSHIP FORM */}
        {normFamily === 'FEATURE_RELATIONSHIP' && (
          <div className="form-vertical">
            <div className="form-group">
              <label>Select Features for Correlation Analysis (min 2) *</label>
              <div className="feature-checkbox-grid">
                {featureNames.map((name) => {
                  const checked = relFeatures.includes(name)
                  return (
                    <label key={name} className="checkbox-item font-mono text-xs">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setRelFeatures([...relFeatures, name])
                          } else {
                            setRelFeatures(relFeatures.filter((f) => f !== name))
                          }
                        }}
                      />
                      {name}
                    </label>
                  )
                })}
              </div>
            </div>

            <div className="form-grid mt-2">
              <div className="form-group">
                <label>Max Scatter Points Rendered</label>
                <select
                  value={relMaxScatter}
                  onChange={(e) => setRelMaxScatter(parseInt(e.target.value, 10) || 1000)}
                >
                  <option value={500}>500 points (Fast)</option>
                  <option value={1000}>1,000 points (Balanced)</option>
                  <option value={3000}>3,000 points (Dense)</option>
                </select>
              </div>

              <div className="form-group">
                <label>Missing Value Policy</label>
                <select
                  value={missingValuePolicy}
                  onChange={(e) => setMissingValuePolicy(e.target.value)}
                >
                  <option value="CompleteCase">Complete Case (Row-wise)</option>
                  <option value="PairwiseComplete">Pairwise Complete (Per pair)</option>
                </select>
              </div>
            </div>
          </div>
        )}

        {/* K-MEANS FORM */}
        {normFamily === 'KMEANS' && (
          <div className="form-vertical">
            <div className="form-group">
              <label>Select Features for K-Means Clustering (min 2) *</label>
              <div className="feature-checkbox-grid">
                {featureNames.map((name) => {
                  const checked = kmeansFeatures.includes(name)
                  return (
                    <label key={name} className="checkbox-item font-mono text-xs">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setKmeansFeatures([...kmeansFeatures, name])
                          } else {
                            setKmeansFeatures(kmeansFeatures.filter((f) => f !== name))
                          }
                        }}
                      />
                      {name}
                    </label>
                  )
                })}
              </div>
            </div>

            <div className="form-grid mt-2">
              <div className="form-group">
                <label>Number of Clusters (K: 2 - 10)</label>
                <input
                  type="number"
                  min={2}
                  max={10}
                  value={kmeansK}
                  onChange={(e) => setKmeansK(parseInt(e.target.value, 10) || 3)}
                  required
                />
              </div>

              <div className="form-group">
                <label>Random Seed</label>
                <input
                  type="number"
                  value={seed}
                  onChange={(e) => setSeed(parseInt(e.target.value, 10) || 42)}
                />
              </div>

              <div className="form-group">
                <label>Feature Scaling</label>
                <select
                  value={scalingPolicy}
                  onChange={(e) => setScalingPolicy(e.target.value)}
                >
                  <option value="Standard">Standard (Z-Score zero-mean unit-variance)</option>
                  <option value="Robust">Robust (Median / IQR)</option>
                  <option value="None">None (Raw scales)</option>
                </select>
              </div>

              <div className="form-group">
                <label>Missing Value Policy</label>
                <select
                  value={missingValuePolicy}
                  onChange={(e) => setMissingValuePolicy(e.target.value)}
                >
                  <option value="CompleteCase">Complete Case</option>
                  <option value="MedianImpute">Median Imputation</option>
                </select>
              </div>
            </div>
          </div>
        )}

        {/* GMM FORM */}
        {normFamily === 'GMM' && (
          <div className="form-vertical">
            <div className="form-group">
              <label>Select Features for Gaussian Mixture Model (min 2) *</label>
              <div className="feature-checkbox-grid">
                {featureNames.map((name) => {
                  const checked = gmmFeatures.includes(name)
                  return (
                    <label key={name} className="checkbox-item font-mono text-xs">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setGmmFeatures([...gmmFeatures, name])
                          } else {
                            setGmmFeatures(gmmFeatures.filter((f) => f !== name))
                          }
                        }}
                      />
                      {name}
                    </label>
                  )
                })}
              </div>
            </div>

            <div className="form-grid mt-2">
              <div className="form-group">
                <label>Mixture Components (2 - 8)</label>
                <input
                  type="number"
                  min={2}
                  max={8}
                  value={gmmComponents}
                  onChange={(e) => setGmmComponents(parseInt(e.target.value, 10) || 3)}
                  required
                />
              </div>

              <div className="form-group">
                <label>Covariance Structure</label>
                <select
                  value={gmmCovariance}
                  onChange={(e) => setGmmCovariance(e.target.value as 'diagonal' | 'full')}
                >
                  <option value="diagonal">Diagonal (Independent feature variances)</option>
                  <option value="full">Full (Covariance cross-terms)</option>
                </select>
              </div>

              <div className="form-group">
                <label>Random Initialization Seed</label>
                <input
                  type="number"
                  value={seed}
                  onChange={(e) => setSeed(parseInt(e.target.value, 10) || 42)}
                />
              </div>

              <div className="form-group">
                <label>Feature Scaling</label>
                <select
                  value={scalingPolicy}
                  onChange={(e) => setScalingPolicy(e.target.value)}
                >
                  <option value="Standard">Standard (Z-Score)</option>
                  <option value="None">None (Raw)</option>
                </select>
              </div>
            </div>
          </div>
        )}

        {/* ISOLATION FOREST FORM */}
        {normFamily === 'ISOLATION_FOREST' && (
          <div className="form-vertical">
            <div className="form-group">
              <label>Select Features for Anomaly Isolation (min 2) *</label>
              <div className="feature-checkbox-grid">
                {featureNames.map((name) => {
                  const checked = ifFeatures.includes(name)
                  return (
                    <label key={name} className="checkbox-item font-mono text-xs">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setIfFeatures([...ifFeatures, name])
                          } else {
                            setIfFeatures(ifFeatures.filter((f) => f !== name))
                          }
                        }}
                      />
                      {name}
                    </label>
                  )
                })}
              </div>
            </div>

            <div className="form-grid mt-2">
              <div className="form-group">
                <label>Trees Count (50 - 300)</label>
                <input
                  type="number"
                  min={20}
                  max={300}
                  value={ifTrees}
                  onChange={(e) => setIfTrees(parseInt(e.target.value, 10) || 100)}
                />
              </div>

              <div className="form-group">
                <label>Subsample Size (64 - 512)</label>
                <input
                  type="number"
                  min={32}
                  max={512}
                  value={ifSubsample}
                  onChange={(e) => setIfSubsample(parseInt(e.target.value, 10) || 256)}
                />
              </div>

              <div className="form-group">
                <label>Expected Contamination Rate (0.01 - 0.50)</label>
                <select
                  value={ifContamination}
                  onChange={(e) => setIfContamination(parseFloat(e.target.value) || 0.1)}
                >
                  <option value={0.01}>1% (Extreme outliers)</option>
                  <option value={0.05}>5% (Conservative)</option>
                  <option value={0.10}>10% (Standard default)</option>
                  <option value={0.20}>20% (Aggressive inspection)</option>
                </select>
              </div>

              <div className="form-group">
                <label>Random Seed</label>
                <input
                  type="number"
                  value={seed}
                  onChange={(e) => setSeed(parseInt(e.target.value, 10) || 42)}
                />
              </div>
            </div>
          </div>
        )}

        {/* SCRIPT CONDITION FORM */}
        {normFamily === 'SCRIPT_CONDITION' && (
          <div className="form-vertical">
            {/* Guide notice banner */}
            <div
              style={{
                background: 'rgba(59, 130, 246, 0.1)',
                border: '1px solid rgba(59, 130, 246, 0.3)',
                borderRadius: 6,
                padding: '10px 14px',
                marginBottom: 12,
                fontSize: 13,
                color: '#93c5fd',
              }}
            >
              <strong>📍 AOI Script Editor:</strong> Paste Python-like classifier script for this defect below.
              The engine evaluates individual logic conditions (Guard clauses, IP_Cond, Cond1, Cond2, DL_Cond...)
              and breaks down pass/fail outcomes across defect observations.
            </div>

            {/* Quick Action Toolbar */}
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center', marginBottom: 10 }}>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setScriptText(DEFAULT_AOI_LIGHT_FAIL_SCRIPT)}
                title="Paste template script for AOI Light_Fail (Priority 0)"
              >
                ✨ Template: Light_Fail (Priority 0)
              </button>

              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setScriptText(DEFAULT_AOI_DIC_CRACK_SCRIPT)}
                title="Paste template script for AOI DIC_Crack (Priority 1)"
              >
                ✨ Template: DIC_Crack (Priority 1)
              </button>

              {scriptText.includes('"Rules"') && (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  style={{ background: '#10b981', borderColor: '#059669', color: '#fff', fontWeight: 600 }}
                  onClick={() => void handleImportRulesJsonText(scriptText)}
                  disabled={savingRuleScript}
                  title="Detected AOI Rules JSON structure! Click to import all rules into database"
                >
                  📥 Import All Rules JSON
                </button>
              )}

              {resolvedRule?.scriptContent && (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => setScriptText(resolvedRule.scriptContent || '')}
                  title={`Load script saved in Rule ${resolvedRule.ruleCode}`}
                >
                  📋 Load from Rule ({resolvedRule.ruleCode})
                </button>
              )}

              {resolvedRule?.id && (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => void handleSaveScriptToRule()}
                  disabled={savingRuleScript || !scriptText.trim()}
                  title={`Save this script into Rule ${resolvedRule.ruleCode}`}
                >
                  {savingRuleScript ? 'Saving...' : `💾 Save to Rule ${resolvedRule.ruleCode}`}
                </button>
              )}

              {scriptText && (
                <button
                  type="button"
                  className="btn btn-ghost btn-sm text-slate-400"
                  onClick={() => setScriptText('')}
                >
                  Clear
                </button>
              )}

              {resolvedRule && (
                <span className="font-mono text-xs ml-auto" style={{ color: '#94a3b8' }}>
                  Matched Rule: <strong style={{ color: '#60a5fa' }}>{resolvedRule.ruleCode}</strong> v{resolvedRule.version}
                </span>
              )}
            </div>

            {saveScriptMessage && (
              <div
                style={{
                  padding: '6px 10px',
                  borderRadius: 4,
                  marginBottom: 8,
                  fontSize: 12,
                  background: saveScriptMessage.startsWith('✓') ? 'rgba(34, 197, 94, 0.15)' : 'rgba(239, 68, 68, 0.15)',
                  border: `1px solid ${saveScriptMessage.startsWith('✓') ? '#22c55e' : '#ef4444'}`,
                  color: saveScriptMessage.startsWith('✓') ? '#22c55e' : '#ef4444',
                }}
              >
                {saveScriptMessage}
              </div>
            )}

            <div className="form-group">
              <label>AOI Classifier Script (Python-like) *</label>
              <textarea
                value={scriptText}
                onChange={(e) => setScriptText(e.target.value)}
                placeholder={`if( G010_VpID != 1):\n\treturn False\n\nCond1 = ( G020_PxlArea > 90 )\nCond2 = ( G038_PrcGrayMin < 45 )\nDL_Cond = MLClass01 > 90\n\nreturn Cond1 and Cond2 and DL_Cond`}
                required
                rows={16}
                style={{
                  fontFamily: 'monospace',
                  fontSize: 12,
                  background: 'rgba(0,0,0,0.3)',
                  color: '#e2e8f0',
                  border: '1px solid rgba(255,255,255,0.1)',
                  borderRadius: 6,
                  padding: 10,
                  width: '100%',
                  resize: 'vertical',
                  whiteSpace: 'pre',
                  tabSize: 4,
                }}
              />
            </div>

            <div className="form-grid mt-2">
              <div className="form-group">
                <label>Max Evaluation Rows (per-row detail limit)</label>
                <input
                  type="number"
                  min={10}
                  max={5000}
                  value={maxEvalRows}
                  onChange={(e) => setMaxEvalRows(parseInt(e.target.value, 10) || 500)}
                />
                <span className="text-xs" style={{ color: '#94a3b8' }}>
                  Aggregated statistics always cover all rows. This limits per-row detail output.
                </span>
              </div>
            </div>
          </div>
        )}

        <div className="config-form-actions mt-4">
          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting || isBlocked}
          >
            {submitting ? 'Executing Job...' : `Run ${normFamily} Analytics`}
          </button>
        </div>
      </form>
    </div>
  )
}
