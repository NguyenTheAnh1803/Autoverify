import React, { useEffect, useMemo, useState } from 'react'
import { Button, Field, Icon } from '../ui'

export type GuardItem = {
  id: string
  feature: string
  operator: '!=' | '==' | '<' | '>' | '<=' | '>='
  value: number
}

export type ConditionItem = {
  id: string
  name: string
  feature: string
  operator: '>' | '<' | '>=' | '<=' | '==' | '!='
  value: number
}

export type VisualRuleBuilderProps = {
  script: string
  onChange: (newScript: string) => void
  readOnly?: boolean
}

const COMMON_FEATURES = [
  { value: 'G010_VpID', label: 'G010_VpID (Camera Angle / VP)' },
  { value: 'G008_LightID', label: 'G008_LightID (Lighting Channel)' },
  { value: 'G009_RoiID', label: 'G009_RoiID (Region of Interest / ROI)' },
  { value: 'G020_PxlArea', label: 'G020_PxlArea (Defect Area in Pixels)' },
  { value: 'G017_PxlWidth', label: 'G017_PxlWidth (Defect Width in Pixels)' },
  { value: 'G018_PxlHeight', label: 'G018_PxlHeight (Defect Height in Pixels)' },
  { value: 'G062_OrgGrayMax', label: 'G062_OrgGrayMax (Max Gray Intensity)' },
  { value: 'G064_OrgGrayStdDev', label: 'G064_OrgGrayStdDev (Gray Std Dev / Contrast)' },
  { value: 'G038_PrcGrayMin', label: 'G038_PrcGrayMin (Min Processed Gray)' },
  { value: 'ConfidenceScore', label: 'ConfidenceScore (AOI Machine Confidence)' },
  { value: 'MLClass01', label: 'MLClass01 (Deep Learning Confidence)' },
]

export const VisualRuleBuilder: React.FC<VisualRuleBuilderProps> = ({ script, onChange, readOnly }) => {
  const [mode, setMode] = useState<'visual' | 'code'>('visual')

  // Visual state
  const [guards, setGuards] = useState<GuardItem[]>([])
  const [conditions, setConditions] = useState<ConditionItem[]>([])
  const [combiner, setCombiner] = useState<'all' | 'any' | 'custom'>('all')
  const [customFormula, setCustomFormula] = useState<string>('')

  // Parse script to visual items on initial mount or when switching to visual mode
  useEffect(() => {
    if (!script.trim()) {
      setGuards([])
      setConditions([
        { id: 'c1', name: 'Cond1', feature: 'G020_PxlArea', operator: '>', value: 90 },
      ])
      setCombiner('all')
      setCustomFormula('Cond1')
      return
    }

    // Try parsing existing script
    try {
      const lines = script.split('\n').map((l) => l.trim()).filter(Boolean)
      const parsedGuards: GuardItem[] = []
      const parsedConditions: ConditionItem[] = []
      let parsedReturn = ''

      for (let i = 0; i < lines.length; i++) {
        const line = lines[i]
        // Match simple guard: if( Feature != Val): \n return False
        const guardMatch = line.match(/^if\s*\(\s*(\w+)\s*(!=|==|>|<|>=|<=)\s*([\d.]+)\s*\)\s*:\s*$/i)
        if (guardMatch) {
          const next = lines[i + 1]
          if (next && /^return\s+False$/i.test(next)) {
            parsedGuards.push({
              id: 'g_' + i,
              feature: guardMatch[1],
              operator: guardMatch[2] as any,
              value: parseFloat(guardMatch[3]),
            })
            i++ // skip return False
            continue
          }
        }

        // Match condition: CondName = (Feature Op Val)
        const condMatch = line.match(/^(\w+)\s*=\s*\(?\s*(\w+)\s*(>|<|>=|<=|==|!=)\s*([\d.]+)\s*\)?$/i)
        if (condMatch) {
          parsedConditions.push({
            id: 'c_' + i,
            name: condMatch[1],
            feature: condMatch[2],
            operator: condMatch[3] as any,
            value: parseFloat(condMatch[4]),
          })
          continue
        }

        // Match return statement
        const retMatch = line.match(/^return\s+(.+)$/i)
        if (retMatch) {
          parsedReturn = retMatch[1].trim()
        }
      }

      if (parsedConditions.length > 0) {
        setGuards(parsedGuards)
        setConditions(parsedConditions)
        if (parsedReturn.includes(' or ') && !parsedReturn.includes(' and ')) {
          setCombiner('any')
        } else if (parsedReturn.includes(' and ') && !parsedReturn.includes(' or ')) {
          setCombiner('all')
        } else if (parsedReturn) {
          setCombiner('custom')
          setCustomFormula(parsedReturn)
        }
      }
    } catch {
      // Keep script as is
    }
  }, [])

  // Generate python script from visual state
  const generatedScript = useMemo(() => {
    const parts: string[] = []

    if (guards.length > 0) {
      parts.push('# Guard clauses (Early exit filters)')
      guards.forEach((g) => {
        parts.push(`if( ${g.feature} ${g.operator} ${g.value} ):\n\treturn False`)
      })
      parts.push('')
    }

    if (conditions.length > 0) {
      parts.push('# Optical defect conditions')
      conditions.forEach((c) => {
        parts.push(`${c.name} = ( ${c.feature} ${c.operator} ${c.value} )`)
      })
      parts.push('')

      if (combiner === 'all') {
        parts.push(`return ${conditions.map((c) => c.name).join(' and ')}`)
      } else if (combiner === 'any') {
        parts.push(`return ${conditions.map((c) => c.name).join(' or ')}`)
      } else {
        parts.push(`return ${customFormula.trim() || conditions.map((c) => c.name).join(' and ')}`)
      }
    } else {
      parts.push('return False')
    }

    return parts.join('\n')
  }, [guards, conditions, combiner, customFormula])

  // Sync visual changes to parent onChange
  function applyVisualToScript() {
    onChange(generatedScript)
  }

  function handleAddGuard() {
    setGuards([
      ...guards,
      { id: 'g_' + Date.now(), feature: 'G010_VpID', operator: '!=', value: 1 },
    ])
  }

  function handleRemoveGuard(id: string) {
    setGuards(guards.filter((g) => g.id !== id))
  }

  function handleAddCondition() {
    const nextIdx = conditions.length + 1
    setConditions([
      ...conditions,
      {
        id: 'c_' + Date.now(),
        name: `Cond${nextIdx}`,
        feature: 'G020_PxlArea',
        operator: '>',
        value: 90,
      },
    ])
  }

  function handleRemoveCondition(id: string) {
    setConditions(conditions.filter((c) => c.id !== id))
  }

  return (
    <div className="visual-rule-builder panel vstack gap-md" style={{ border: '1px solid var(--slate-200)', padding: '14px' }}>
      {/* Header with Mode Toggle */}
      <div className="hstack justify-between" style={{ borderBottom: '1px solid var(--slate-100)', paddingBottom: '10px' }}>
        <div className="hstack gap-sm" style={{ alignItems: 'center' }}>
          <strong className="text-sm text-strong">Rule Script Editor (Rule Studio 2.0)</strong>
          <span className="tag-badge text-xs" style={{ background: 'var(--status-info-bg)', color: 'var(--blue-700)' }}>
            No-Code + Python Hybrid
          </span>
        </div>
        <div className="hstack gap-xs">
          <button
            type="button"
            className={`ghost-button compact ${mode === 'visual' ? 'active' : ''}`}
            style={{ fontWeight: mode === 'visual' ? 600 : 400 }}
            onClick={() => {
              setMode('visual')
            }}
          >
            <Icon name="check-circle" className="icon inline-icon" /> Visual Mode (No-Code)
          </button>
          <button
            type="button"
            className={`ghost-button compact ${mode === 'code' ? 'active' : ''}`}
            style={{ fontWeight: mode === 'code' ? 600 : 400 }}
            onClick={() => {
              if (mode === 'visual') applyVisualToScript()
              setMode('code')
            }}
          >
            <Icon name="help" className="icon inline-icon" /> Code Mode (Python Script)
          </button>
        </div>
      </div>

      {mode === 'visual' ? (
        <div className="vstack gap-lg">
          {/* SECTION 1: GUARD CLAUSES */}
          <div className="vstack gap-sm">
            <div className="hstack justify-between" style={{ alignItems: 'center' }}>
              <div>
                <strong className="text-xs text-strong" style={{ textTransform: 'uppercase' }}>
                  1. Early Exit Guards (Góc chụp / ROI / Kênh đèn yêu cầu)
                </strong>
                <p className="text-xs text-muted" style={{ margin: 0 }}>
                  Nếu thông số không khớp điều kiện, máy lập tức bỏ qua (Return False) mà không tốn công tính toán.
                </p>
              </div>
              {!readOnly && (
                <Button type="button" variant="ghost" size="sm" onClick={handleAddGuard}>
                  + Thêm Guard
                </Button>
              )}
            </div>

            {guards.length === 0 ? (
              <div className="text-xs text-muted" style={{ fontStyle: 'italic', padding: '6px' }}>
                Không có Guard (Kiểm tra trên mọi góc chụp và điều kiện đèn).
              </div>
            ) : (
              <div className="vstack gap-xs">
                {guards.map((g, idx) => (
                  <div key={g.id} className="hstack gap-sm" style={{ alignItems: 'center', background: 'var(--slate-50)', padding: '6px 10px', borderRadius: '6px' }}>
                    <span className="text-xs font-mono text-muted">IF</span>
                    <select
                      value={g.feature}
                      disabled={readOnly}
                      onChange={(e) => {
                        const updated = [...guards]
                        updated[idx].feature = e.target.value
                        setGuards(updated)
                      }}
                      style={{ minWidth: '180px' }}
                    >
                      {COMMON_FEATURES.map((f) => (
                        <option key={f.value} value={f.value}>{f.label}</option>
                      ))}
                    </select>

                    <select
                      value={g.operator}
                      disabled={readOnly}
                      onChange={(e) => {
                        const updated = [...guards]
                        updated[idx].operator = e.target.value as any
                        setGuards(updated)
                      }}
                      style={{ width: '70px' }}
                    >
                      <option value="!=">!=</option>
                      <option value="==">==</option>
                      <option value="<">&lt;</option>
                      <option value=">">&gt;</option>
                      <option value="<=">&lt;=</option>
                      <option value=">=">&gt;=</option>
                    </select>

                    <input
                      type="number"
                      value={g.value}
                      disabled={readOnly}
                      onChange={(e) => {
                        const updated = [...guards]
                        updated[idx].value = parseFloat(e.target.value) || 0
                        setGuards(updated)
                      }}
                      style={{ width: '90px' }}
                    />

                    <span className="text-xs font-mono text-muted">THEN RETURN FALSE</span>

                    {!readOnly && (
                      <button
                        type="button"
                        className="ghost-button compact text-danger"
                        style={{ marginLeft: 'auto' }}
                        onClick={() => handleRemoveGuard(g.id)}
                        title="Xóa Guard"
                      >
                        ?
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* SECTION 2: FEATURE CONDITIONS */}
          <div className="vstack gap-sm">
            <div className="hstack justify-between" style={{ alignItems: 'center' }}>
              <div>
                <strong className="text-xs text-strong" style={{ textTransform: 'uppercase' }}>
                  2. Khối Điều Kiện Đo Lường Quang Học (Feature Conditions)
                </strong>
                <p className="text-xs text-muted" style={{ margin: 0 }}>
                  Xác định ngưỡng kích thước, độ sáng, độ lệch chuẩn pixel để xác nhận Real NG.
                </p>
              </div>
              {!readOnly && (
                <Button type="button" variant="primary" size="sm" onClick={handleAddCondition}>
                  + Thêm Điều Kiện Đo
                </Button>
              )}
            </div>

            <div className="vstack gap-xs">
              {conditions.map((c, idx) => (
                <div key={c.id} className="hstack gap-sm" style={{ alignItems: 'center', background: 'var(--slate-50)', padding: '8px 12px', borderRadius: '6px' }}>
                  <input
                    type="text"
                    value={c.name}
                    disabled={readOnly}
                    onChange={(e) => {
                      const updated = [...conditions]
                      updated[idx].name = e.target.value
                      setConditions(updated)
                    }}
                    style={{ width: '90px', fontWeight: 'bold' }}
                    placeholder="Cond1"
                  />
                  <span className="text-xs font-mono text-muted">=</span>

                  <select
                    value={c.feature}
                    disabled={readOnly}
                    onChange={(e) => {
                      const updated = [...conditions]
                      updated[idx].feature = e.target.value
                      setConditions(updated)
                    }}
                    style={{ flex: 1, minWidth: '180px' }}
                  >
                    {COMMON_FEATURES.map((f) => (
                      <option key={f.value} value={f.value}>{f.label}</option>
                    ))}
                  </select>

                  <select
                    value={c.operator}
                    disabled={readOnly}
                    onChange={(e) => {
                      const updated = [...conditions]
                      updated[idx].operator = e.target.value as any
                      setConditions(updated)
                    }}
                    style={{ width: '70px', fontWeight: 'bold' }}
                  >
                    <option value=">">&gt;</option>
                    <option value="<">&lt;</option>
                    <option value=">=">&gt;=</option>
                    <option value="<=">&lt;=</option>
                    <option value="==">==</option>
                    <option value="!=">!=</option>
                  </select>

                  <input
                    type="number"
                    step="any"
                    value={c.value}
                    disabled={readOnly}
                    onChange={(e) => {
                      const updated = [...conditions]
                      updated[idx].value = parseFloat(e.target.value) || 0
                      setConditions(updated)
                    }}
                    style={{ width: '110px', fontWeight: 'bold' }}
                  />

                  {!readOnly && (
                    <button
                      type="button"
                      className="ghost-button compact text-danger"
                      style={{ marginLeft: '6px' }}
                      onClick={() => handleRemoveCondition(c.id)}
                      title="Xóa điều kiện"
                    >
                      ?
                    </button>
                  )}
                </div>
              ))}
            </div>
          </div>

          {/* SECTION 3: COMBINER */}
          <div className="vstack gap-xs" style={{ background: 'var(--blue-50)', padding: '12px', borderRadius: '6px', border: '1px solid var(--blue-200)' }}>
            <strong className="text-xs text-strong" style={{ color: 'var(--blue-900)' }}>
              3. Phép Kết Hợp Logic Phán Đoán (Return Verdict Combiner)
            </strong>
            <div className="hstack gap-lg" style={{ marginTop: '4px' }}>
              <label className="hstack gap-xs text-xs" style={{ cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="combiner"
                  checked={combiner === 'all'}
                  disabled={readOnly}
                  onChange={() => setCombiner('all')}
                />
                <strong>Khớp TẤT CẢ (AND)</strong> - Thỏa mãn mọi điều kiện mới tính Real NG
              </label>

              <label className="hstack gap-xs text-xs" style={{ cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="combiner"
                  checked={combiner === 'any'}
                  disabled={readOnly}
                  onChange={() => setCombiner('any')}
                />
                <strong>Khớp BẤT KỲ (OR)</strong> - Chỉ cần 1 điều kiện vi phạm là tính Real NG
              </label>

              <label className="hstack gap-xs text-xs" style={{ cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="combiner"
                  checked={combiner === 'custom'}
                  disabled={readOnly}
                  onChange={() => setCombiner('custom')}
                />
                <strong>Tùy chỉnh công thức</strong>
              </label>
            </div>

            {combiner === 'custom' && (
              <input
                type="text"
                className="font-mono text-xs w-full"
                style={{ marginTop: '6px' }}
                value={customFormula}
                disabled={readOnly}
                onChange={(e) => setCustomFormula(e.target.value)}
                placeholder="VD: Cond1 and (Cond2 or Cond3)"
              />
            )}
          </div>

          {/* LIVE GENERATED SCRIPT PREVIEW */}
          <div className="vstack gap-xs">
            <div className="hstack justify-between" style={{ alignItems: 'center' }}>
              <span className="text-xs font-mono text-muted">Preview Mã Python sinh tự động:</span>
              {!readOnly && (
                <Button type="button" variant="primary" size="sm" onClick={applyVisualToScript}>
                  <Icon name="check-circle" className="icon inline-icon" /> Đồng bộ vào Script
                </Button>
              )}
            </div>
            <pre
              className="font-mono text-xs"
              style={{
                background: 'var(--slate-900)',
                color: '#38bdf8',
                padding: '10px',
                borderRadius: '6px',
                maxHeight: '130px',
                overflowY: 'auto',
                margin: 0,
              }}
            >
              {generatedScript}
            </pre>
          </div>
        </div>
      ) : (
        /* CODE MODE */
        <div className="vstack gap-sm">
          <Field label="Python Classifier Script (Chế độ Code nâng cao)">
            <textarea
              className="font-mono text-xs w-full text-strong"
              value={script}
              disabled={readOnly}
              onChange={(e) => onChange(e.target.value)}
              rows={12}
              style={{
                background: 'var(--slate-950)',
                color: '#a7f3d0',
                padding: '12px',
                lineHeight: 1.5,
                borderRadius: '6px',
              }}
              placeholder={`# Example:\nif( G010_VpID != 1 ):\n\treturn False\n\nCond1 = ( G020_PxlArea > 90 )\nreturn Cond1`}
            />
          </Field>
          <div className="text-xs text-muted">
            Kỹ sư có thể tự do gõ cú pháp Python: hỗ trợ Guard clauses (`if (...) return False`), phép gán điều kiện boolean, và câu lệnh `return`.
          </div>
        </div>
      )}
    </div>
  )
}
