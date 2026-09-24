import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { storageApi } from '../api'
import { formatBytes, formatDate, formString, paginate } from '../format'
import { DataTable, Field, Modal, Pagination } from '../ui'
import type {
  DefectStoragePreviewItem,
  Notice,
  SeaweedFsConfig,
  StorageShardingInfo,
  StorageTestConnectionResult,
  StoredObject,
} from '../types'

export function StorageTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  // Stored objects state
  const [objects, setObjects] = useState<StoredObject[]>([])
  const [jsonOpen, setJsonOpen] = useState(false)
  const [imageOpen, setImageOpen] = useState(false)
  const [objectName, setObjectName] = useState('payload.json')
  const [busy, setBusy] = useState(false)
  const [page, setPage] = useState(1)
  const paged = paginate(objects, page)

  // SeaweedFS configuration state
  const [config, setConfig] = useState<SeaweedFsConfig>({
    masterUrl: 'http://localhost:9333',
    filerUrl: 'http://localhost:8888',
    publicBaseUrl: '',
    defaultBucket: 'autoverify',
    offloadDefects: true,
  })
  const [configBusy, setConfigBusy] = useState(false)
  const [testingConnection, setTestingConnection] = useState(false)
  const [testResult, setTestResult] = useState<StorageTestConnectionResult | null>(null)
  const [shardingInfo, setShardingInfo] = useState<StorageShardingInfo | null>(null)

  // Defect Workbench state
  const [defects, setDefects] = useState<DefectStoragePreviewItem[]>([])
  const [searchKeyword, setSearchKeyword] = useState('')
  const [storageFilter, setStorageFilter] = useState<'all' | 'inline_only' | 'offloaded_only'>('all')
  const [searchLimit, setSearchLimit] = useState(25)
  const [searchingDefects, setSearchingDefects] = useState(false)
  const [migratingSingleId, setMigratingSingleId] = useState<string | null>(null)
  const [batchMigrating, setBatchMigrating] = useState(false)
  const [batchModalOpen, setBatchModalOpen] = useState(false)
  const [batchLimit, setBatchLimit] = useState(50)

  // Product Bundle Packaging Modal state
  const [bundleModalOpen, setBundleModalOpen] = useState(false)
  const [bundlePackaging, setBundlePackaging] = useState(false)
  const [bundleLimit, setBundleLimit] = useState(50)
  const [bundlePurgeInline, setBundlePurgeInline] = useState(true)

  // Product Defect Pack Consolidation Modal state
  const [packModalOpen, setPackModalOpen] = useState(false)
  const [packConsolidating, setPackConsolidating] = useState(false)
  const [packLimit, setPackLimit] = useState(50)
  const [packPurgeInline, setPackPurgeInline] = useState(true)

  // Load existing stored objects
  const loadObjects = useCallback(async () => {
    try {
      setObjects(await storageApi.list())
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load storage objects.' })
    }
  }, [onNotice])

  // Load SeaweedFS config
  const loadConfig = useCallback(async () => {
    try {
      const cfg = await storageApi.getConfig()
      setConfig(cfg)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to fetch SeaweedFS config.' })
    }
  }, [onNotice])

  // Load Defects preview
  const loadDefects = useCallback(async () => {
    setSearchingDefects(true)
    try {
      const list = await storageApi.previewDefects({
        keyword: searchKeyword || undefined,
        filter: storageFilter,
        limit: searchLimit,
      })
      setDefects(list)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load defects preview.' })
    } finally {
      setSearchingDefects(false)
    }
  }, [searchKeyword, storageFilter, searchLimit, onNotice])

  const loadSharding = useCallback(async () => {
    try {
      setShardingInfo(await storageApi.getShardingInfo())
    } catch {
      // Sharding info is optional diagnostic
    }
  }, [])

  useEffect(() => {
    void loadObjects()
    void loadConfig()
    void loadDefects()
    void loadSharding()
  }, [loadObjects, loadConfig, loadDefects, loadSharding])

  // Save SeaweedFS configuration
  async function handleSaveConfig(e: FormEvent) {
    e.preventDefault()
    setConfigBusy(true)
    try {
      const updated = await storageApi.updateConfig(config)
      setConfig(updated)
      onNotice({ tone: 'success', message: 'SeaweedFS configuration saved and persisted to appsettings.json.' })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to save configuration.' })
    } finally {
      setConfigBusy(false)
    }
  }

  // Test SeaweedFS connection
  async function handleTestConnection() {
    setTestingConnection(true)
    try {
      const res = await storageApi.testConnection(config)
      setTestResult(res)
      if (res.success) {
        onNotice({ tone: 'success', message: `SeaweedFS connection test passed: ${res.statusMessage}` })
      } else {
        onNotice({ tone: 'error', message: `SeaweedFS connection degraded or unreachable: ${res.statusMessage}` })
      }
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Connection test request failed.' })
    } finally {
      setTestingConnection(false)
    }
  }

  // Single defect migration
  async function handleMigrateSingle(defectId: string) {
    setMigratingSingleId(defectId)
    try {
      const res = await storageApi.migrateSingleDefect(defectId)
      if (res.success) {
        onNotice({
          tone: 'success',
          message: `Defect offloaded successfully to SeaweedFS: ${res.featureUrl || res.customFeatureUrl || res.message}`,
        })
        await loadDefects()
        await loadObjects()
      } else {
        onNotice({ tone: 'error', message: `Offload failed: ${res.message}` })
      }
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Migration request failed.' })
    } finally {
      setMigratingSingleId(null)
    }
  }

  // Batch defect migration
  async function handleMigrateBatch(allWithInline: boolean) {
    setBatchMigrating(true)
    try {
      const res = await storageApi.migrateBatchDefects({
        allWithInlineJson: allWithInline,
        limit: batchLimit,
      })
      setBatchModalOpen(false)
      onNotice({
        tone: res.failedCount > 0 ? 'error' : 'success',
        message: `Batch Migration Finished: ${res.successCount} migrated to SeaweedFS, ${res.failedCount} failed. (${formatBytes(res.bytesOffloaded)} freed in MariaDB)`,
      })
      await loadDefects()
      await loadObjects()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Batch migration failed.' })
    } finally {
      setBatchMigrating(false)
    }
  }

  // Product bundle migration (Packaging 10~50 defects/images per product ID into a sharded zip bundle)
  async function handleMigrateBundles() {
    setBundlePackaging(true)
    try {
      const res = await storageApi.migrateBundles({
        allWithInlineJson: bundlePurgeInline,
        limit: bundleLimit,
      })
      setBundleModalOpen(false)
      const savedBytes = Math.max(0, res.totalOriginalBytes - res.totalCompressedBytes)
      onNotice({
        tone: res.successfulBundlesCount > 0 ? 'success' : 'info',
        message: `Bundle Packaging Complete: ${res.successfulBundlesCount} ZIP archives created for ${res.totalProductsProcessed} products (${res.totalDefectsOffloaded} defects packaged, ${formatBytes(savedBytes)} saved, ${res.overallCompressionRatio.toFixed(1)}% ratio).`,
      })
      await loadDefects()
      await loadObjects()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Product bundle packaging failed.' })
    } finally {
      setBundlePackaging(false)
    }
  }

  // Product Defect Pack migration (Consolidate all defects per product into 1 unified JSON pack)
  async function handleMigratePacks() {
    setPackConsolidating(true)
    try {
      const res = await storageApi.migratePacks({
        allWithInlineJson: packPurgeInline,
        limit: packLimit,
      })
      setPackModalOpen(false)
      onNotice({
        tone: res.successCount > 0 ? 'success' : 'info',
        message: `Defect Pack Consolidation Complete: ${res.successCount} unified JSON packs created (${res.totalKeyIdsProcessed} products processed, ${res.totalDefectsProcessed} defects consolidated, ${res.failedCount} failures).`,
      })
      await loadDefects()
      await loadObjects()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Defect pack consolidation failed.' })
    } finally {
      setPackConsolidating(false)
    }
  }

  async function uploadJson(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      const payloadText = formString(event, 'payload')
      const payload = JSON.parse(payloadText || '{}') as unknown
      await storageApi.uploadJson({
        bucket: formString(event, 'bucket'),
        objectName: formString(event, 'objectName'),
        payload,
      })
      setJsonOpen(false)
      onNotice({ tone: 'success', message: 'JSON object stored in SeaweedFS.' })
      await loadObjects()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'JSON upload failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function uploadImage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const file = (event.currentTarget.elements.namedItem('file') as HTMLInputElement).files?.[0]
    if (!file) {
      onNotice({ tone: 'error', message: 'Image file is required.' })
      return
    }
    setBusy(true)
    try {
      await storageApi.uploadImage(file, formString(event, 'bucket') || undefined)
      setImageOpen(false)
      onNotice({ tone: 'success', message: `Uploaded ${file.name} to SeaweedFS.` })
      await loadObjects()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Image upload failed.' })
    } finally {
      setBusy(false)
    }
  }

  // Summary counts for metrics
  const inlineDefectsCount = defects.filter((d) => d.hasInlineFeatureJson).length
  const offloadedDefectsCount = defects.filter((d) => !d.hasInlineFeatureJson && (d.featureUrl || d.customFeatureUrl)).length
  const checksList = testResult ? [testResult.filer, testResult.master, testResult.writeReadDeleteTest] : []

  return (
    <div className="tab-stack">
      {/* Top Section / Header Metrics */}
      <div className="metrics-grid">
        <article className={`metric-card ${testResult?.success ? 'success' : testResult ? 'warning' : 'info'}`}>
          <span>SeaweedFS Status</span>
          <strong>{testResult ? (testResult.success ? 'ONLINE' : 'DEGRADED') : 'CONFIGURED'}</strong>
          <small>{config.filerUrl}</small>
        </article>
        <article className="metric-card info">
          <span>Target Bucket</span>
          <strong style={{ fontSize: '18px' }}>{config.defaultBucket}</strong>
          <small>{config.offloadDefects ? 'Auto-offload enabled' : 'Manual offload only'}</small>
        </article>
        <article className={`metric-card ${inlineDefectsCount > 0 ? 'warning' : 'success'}`}>
          <span>Local DB JSONs (Sample)</span>
          <strong>{inlineDefectsCount}</strong>
          <small>Ready for SeaweedFS offload</small>
        </article>
        <article className="metric-card success">
          <span>SeaweedFS Linked (Sample)</span>
          <strong>{offloadedDefectsCount}</strong>
          <small>Stored externally</small>
        </article>
      </div>

      {/* Path Sharding & Deep Compression Architecture Banner */}
      <div
        style={{
          background: 'linear-gradient(135deg, #0f172a 0%, #1e293b 100%)',
          color: '#f8fafc',
          borderRadius: '10px',
          padding: '16px 20px',
          border: '1px solid #334155',
          boxShadow: '0 4px 12px rgba(0,0,0,0.08)',
          display: 'grid',
          gap: '12px',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ fontSize: '22px' }}>🛡️</span>
            <div>
              <strong style={{ fontSize: '15px', letterSpacing: '0.2px' }}>
                Deep Compression & 4-Tier Path Sharding Architecture
              </strong>
              <div style={{ fontSize: '12px', color: '#94a3b8' }}>
                Prevents filesystem inode exhaustion & directory lock contention (Max 256 folders per tier).
              </div>
            </div>
          </div>
          <div style={{ display: 'flex', gap: '8px' }}>
            <span style={{ background: '#0284c7', color: '#ffffff', padding: '3px 8px', borderRadius: '4px', fontSize: '11px', fontWeight: 600 }}>
              GZIP Compression (Optimal)
            </span>
            <span style={{ background: '#0d9488', color: '#ffffff', padding: '3px 8px', borderRadius: '4px', fontSize: '11px', fontWeight: 600 }}>
              Product ZIP Bundling
            </span>
            <span style={{ background: '#6366f1', color: '#ffffff', padding: '3px 8px', borderRadius: '4px', fontSize: '11px', fontWeight: 600 }}>
              On-Demand Streaming
            </span>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '14px', paddingTop: '6px' }}>
          <div style={{ background: 'rgba(255,255,255,0.05)', padding: '10px 14px', borderRadius: '6px', fontSize: '12px' }}>
            <div style={{ color: '#38bdf8', fontWeight: 600, marginBottom: '4px' }}>Hierarchical Hash Sharding</div>
            <div style={{ color: '#cbd5e1' }}>
              MD5 hash partitioned into 4 levels of 2 hex chars:
              <br />
              <code style={{ background: 'rgba(0,0,0,0.3)', padding: '2px 6px', borderRadius: '3px', color: '#a5f3fc' }}>
                {shardingInfo?.samplePath || '/defects/a1/b2/c3/d4/prod123/defect_feature.json.gz'}
              </code>
            </div>
          </div>

          <div style={{ background: 'rgba(255,255,255,0.05)', padding: '10px 14px', borderRadius: '6px', fontSize: '12px' }}>
            <div style={{ color: '#2dd4bf', fontWeight: 600, marginBottom: '4px' }}>Product Defect & Image Bundle</div>
            <div style={{ color: '#cbd5e1' }}>
              Packages 10~50 defect feature JSONs + images per product into single ZIP:
              <br />
              <code style={{ background: 'rgba(0,0,0,0.3)', padding: '2px 6px', borderRadius: '3px', color: '#99f6e4' }}>
                {shardingInfo?.bundleSamplePath || '/defects/a1/b2/c3/d4/prod123/prod123_bundle.zip'}
              </code>
            </div>
          </div>

          <div style={{ background: 'rgba(255,255,255,0.05)', padding: '10px 14px', borderRadius: '6px', fontSize: '12px' }}>
            <div style={{ color: '#c084fc', fontWeight: 600, marginBottom: '4px' }}>Unified Defect Pack (1 Pack / Product)</div>
            <div style={{ color: '#cbd5e1' }}>
              Consolidates all defect NG rows for 1 Product ID into a single compressed JSON pack:
              <br />
              <code style={{ background: 'rgba(0,0,0,0.3)', padding: '2px 6px', borderRadius: '3px', color: '#e9d5ff' }}>
                {shardingInfo?.samplePath ? shardingInfo.samplePath.replace(/[^/]+$/, '{keyId}_defects.json.gz') : '/defects/a1/b2/c3/d4/{keyId}_defects.json.gz'}
              </code>
            </div>
          </div>
        </div>
      </div>

      {/* Section 1: SeaweedFS Connection Configuration & Live Testing */}
      <section className="panel">
        <div className="section-heading" style={{ marginBottom: '14px' }}>
          <div>
            <p className="eyebrow">SEAWEEDFS STORAGE CONFIGURATION & TEST</p>
            <h2 style={{ fontSize: '18px', margin: 0 }}>SeaweedFS Cluster Connection</h2>
          </div>
          <div className="actions">
            <button
              type="button"
              className="secondary"
              disabled={testingConnection}
              onClick={() => void handleTestConnection()}
              style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
            >
              {testingConnection ? '⏳ Testing Connection...' : '🔌 Test Connection'}
            </button>
            <button
              type="button"
              disabled={configBusy}
              onClick={(e) => void handleSaveConfig(e)}
              style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
            >
              {configBusy ? '💾 Saving...' : '💾 Save Settings'}
            </button>
          </div>
        </div>

        <form onSubmit={(e) => void handleSaveConfig(e)} style={{ display: 'grid', gap: '14px' }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '12px' }}>
            <Field label="Filer URL (HTTP File API)" required>
              <input
                type="text"
                placeholder="http://localhost:8888"
                value={config.filerUrl}
                onChange={(e) => setConfig({ ...config, filerUrl: e.target.value })}
                required
              />
            </Field>
            <Field label="Master URL (Cluster Topology)" required>
              <input
                type="text"
                placeholder="http://localhost:9333"
                value={config.masterUrl}
                onChange={(e) => setConfig({ ...config, masterUrl: e.target.value })}
                required
              />
            </Field>
            <Field label="Public / Download Base URL (Optional)">
              <input
                type="text"
                placeholder="Leave blank to use Filer URL"
                value={config.publicBaseUrl ?? ''}
                onChange={(e) => setConfig({ ...config, publicBaseUrl: e.target.value })}
              />
            </Field>
            <Field label="Default Bucket Name" required>
              <input
                type="text"
                placeholder="autoverify"
                value={config.defaultBucket}
                onChange={(e) => setConfig({ ...config, defaultBucket: e.target.value })}
                required
              />
            </Field>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginTop: '2px' }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '13px', fontWeight: 600 }}>
              <input
                type="checkbox"
                style={{ width: '16px', height: '16px' }}
                checked={config.offloadDefects}
                onChange={(e) => setConfig({ ...config, offloadDefects: e.target.checked })}
              />
              Automatically offload Defect Feature JSON to SeaweedFS on new ingestion
            </label>
          </div>
        </form>

        {/* Live Diagnostics Card */}
        {testResult && (
          <div
            style={{
              marginTop: '16px',
              padding: '14px',
              borderRadius: '10px',
              background: testResult.success ? '#f0fdf4' : '#fffbeb',
              border: `1px solid ${testResult.success ? '#bbf7d0' : '#fde68a'}`,
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <span style={{ fontSize: '16px' }}>{testResult.success ? '✅' : '⚠️'}</span>
                <strong style={{ color: testResult.success ? '#166534' : '#92400e', fontSize: '14px' }}>
                  {testResult.statusMessage}
                </strong>
              </div>
              <span style={{ fontSize: '11px', color: '#64748b' }}>
                Probe Latency: <strong>{testResult.writeReadDeleteTest.responseTimeMs} ms</strong>
              </span>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '10px' }}>
              {checksList.map((chk, idx) => (
                <div
                  key={idx}
                  style={{
                    background: '#fff',
                    padding: '10px 12px',
                    borderRadius: '8px',
                    border: `1px solid ${chk.reachable ? '#dcfce7' : '#fee2e2'}`,
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '4px',
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <strong style={{ fontSize: '12px', color: '#1e293b' }}>{chk.name}</strong>
                    <span
                      className={`judge-badge ${chk.reachable ? 'ok' : 'ng'}`}
                      style={{ fontSize: '9px', padding: '1px 6px' }}
                    >
                      {chk.reachable ? 'PASS' : 'FAIL'}
                    </span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '4px' }}>
                    <span style={{ fontSize: '11px', color: chk.reachable ? '#15803d' : '#b91c1c' }}>{chk.details}</span>
                    <span style={{ fontSize: '10px', color: '#94a3b8' }}>{chk.responseTimeMs}ms</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </section>

      {/* Section 2: Defect Offload & Migration Workbench */}
      <section className="panel">
        <div className="section-heading" style={{ marginBottom: '14px' }}>
          <div>
            <p className="eyebrow">MARIADB DEFECT SEARCH & SEAWEEDFS MIGRATION</p>
            <h2 style={{ fontSize: '18px', margin: 0 }}>Defect Feature Offload Workbench</h2>
          </div>
          <div className="actions">
            <button
              type="button"
              className="secondary"
              onClick={() => void loadDefects()}
              disabled={searchingDefects}
              style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
            >
              {searchingDefects ? '🔄 Refreshing...' : '🔍 Search / Refresh'}
            </button>
            <button
              type="button"
              onClick={() => setBundleModalOpen(true)}
              style={{
                background: '#0d9488',
                borderColor: '#0d9488',
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
              }}
            >
              📦 Pack Product Bundles (ZIP)
            </button>
            <button
              type="button"
              onClick={() => setBatchModalOpen(true)}
              style={{
                background: '#0284c7',
                borderColor: '#0284c7',
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
              }}
            >
              ⚡ Batch Offload (GZIP)
            </button>
          </div>
        </div>

        {/* Filter Toolbar */}
        <div className="compact-filter-bar" style={{ marginBottom: '14px' }}>
          <Field label="Search Defects (Key ID, Product ID, Defect Name, Table)" className="filter-search">
            <input
              type="text"
              placeholder="e.g. DEFECT_01, KEY-1234, ... "
              value={searchKeyword}
              onChange={(e) => setSearchKeyword(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void loadDefects()
              }}
            />
          </Field>
          <Field label="Storage Filter">
            <select
              value={storageFilter}
              onChange={(e) => setStorageFilter(e.target.value as 'all' | 'inline_only' | 'offloaded_only')}
            >
              <option value="all">All Defects</option>
              <option value="inline_only">Local MariaDB JSON Only (Needs Offload)</option>
              <option value="offloaded_only">SeaweedFS Linked Only</option>
            </select>
          </Field>
          <Field label="Limit">
            <select value={searchLimit} onChange={(e) => setSearchLimit(Number(e.target.value))}>
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </Field>
          <div style={{ display: 'flex', gap: '8px' }}>
            <button
              type="button"
              className="secondary"
              onClick={() => {
                setSearchKeyword('')
                setStorageFilter('all')
              }}
              style={{ height: '38px', padding: '0 12px' }}
            >
              Reset
            </button>
          </div>
        </div>

        {/* Defects Data Table */}
        <DataTable
          columns={['Key ID / Product', 'Defect / Table', 'Judge / Severity', 'Storage State', 'JSON Size', 'SeaweedFS URI', 'Action']}
          rows={defects.map((defect) => {
            const isMigrating = migratingSingleId === defect.id
            return {
              id: defect.id,
              cells: [
                <div key="key">
                  <strong style={{ fontSize: '12px', color: '#1e293b' }}>{defect.keyId}</strong>
                  {defect.productName && (
                    <div style={{ fontSize: '11px', color: '#64748b' }}>{defect.productName}</div>
                  )}
                </div>,
                <div key="defect">
                  <span style={{ fontWeight: 600 }}>{defect.defectName}</span>
                  <div style={{ fontSize: '10px', color: '#64748b' }}>{defect.defectTable}</div>
                </div>,
                <div key="judge" style={{ display: 'flex', gap: '4px', alignItems: 'center' }}>
                  <span className={`judge-badge ${(defect.judge || 'review').toLowerCase()}`}>{defect.judge || 'N/A'}</span>
                  {defect.severity && (
                    <span className="severity-tag" style={{ fontSize: '9px', background: '#e2e8f0', color: '#334155' }}>
                      {defect.severity}
                    </span>
                  )}
                </div>,
                defect.hasInlineFeatureJson ? (
                  <span
                    key="state"
                    style={{
                      background: '#fef3c7',
                      color: '#92400e',
                      border: '1px solid #fde68a',
                      borderRadius: '4px',
                      padding: '2px 6px',
                      fontSize: '10px',
                      fontWeight: 700,
                    }}
                  >
                    💾 Local DB JSON
                  </span>
                ) : (
                  <span
                    key="state"
                    style={{
                      background: '#dcfce7',
                      color: '#166534',
                      border: '1px solid #bbf7d0',
                      borderRadius: '4px',
                      padding: '2px 6px',
                      fontSize: '10px',
                      fontWeight: 700,
                    }}
                  >
                    🌊 SeaweedFS Stored
                  </span>
                ),
                <span key="size" style={{ fontFamily: 'monospace', fontSize: '11px', color: '#475569' }}>
                  {defect.hasInlineFeatureJson ? formatBytes(defect.featureJsonBytes) : 'Offloaded'}
                </span>,
                defect.featureUrl || defect.customFeatureUrl ? (
                  <div key="uri" style={{ display: 'flex', flexDirection: 'column', gap: '3px', maxWidth: '240px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexWrap: 'wrap' }}>
                      {((defect.featureUrl || '').includes('_defects.json.gz#') || (defect.customFeatureUrl || '').includes('_defects.json.gz#')) && (
                        <span style={{ background: '#f5f3ff', color: '#6d28d9', border: '1px solid #ddd6fe', borderRadius: '3px', padding: '1px 4px', fontSize: '9px', fontWeight: 700 }}>
                          📦 DEFECT PACK
                        </span>
                      )}
                      {((defect.featureUrl || '').includes('.zip#') || (defect.customFeatureUrl || '').includes('.zip#')) && (
                        <span style={{ background: '#ccfbf1', color: '#0f766e', border: '1px solid #99f6e4', borderRadius: '3px', padding: '1px 4px', fontSize: '9px', fontWeight: 700 }}>
                          📦 ZIP BUNDLE
                        </span>
                      )}
                      {((defect.featureUrl || '').endsWith('.gz') || (defect.customFeatureUrl || '').endsWith('.gz')) && !((defect.featureUrl || '').includes('_defects.json.gz#')) && (
                        <span style={{ background: '#e0e7ff', color: '#4338ca', border: '1px solid #c7d2fe', borderRadius: '3px', padding: '1px 4px', fontSize: '9px', fontWeight: 700 }}>
                          🗜️ GZIP
                        </span>
                      )}
                      <a
                        href={storageApi.defectFileUrl(defect.id, 'feature')}
                        target="_blank"
                        rel="noreferrer"
                        style={{ fontSize: '10px', color: '#0284c7', textDecoration: 'underline', fontWeight: 600 }}
                        title="Stream & decompress payload directly from SeaweedFS on-demand"
                      >
                        ⚡ Stream File
                      </a>
                      {defect.keyId && (
                        <a
                          href={storageApi.productDefectPackUrl(defect.keyId)}
                          target="_blank"
                          rel="noreferrer"
                          style={{ fontSize: '10px', color: '#7c3aed', textDecoration: 'underline', fontWeight: 600 }}
                          title="Stream & decompress full defect pack for this Product ID"
                        >
                          📦 Pack JSON
                        </a>
                      )}
                    </div>
                    <a
                      href={defect.featureUrl || defect.customFeatureUrl || '#'}
                      target="_blank"
                      rel="noreferrer"
                      style={{
                        fontSize: '11px',
                        color: '#2563eb',
                        textDecoration: 'underline',
                        wordBreak: 'break-all',
                        lineHeight: 1.2,
                      }}
                    >
                      {defect.featureUrl || defect.customFeatureUrl}
                    </a>
                  </div>
                ) : (
                  <span key="uri" style={{ fontSize: '11px', color: '#94a3b8' }}>None</span>
                ),
                defect.hasInlineFeatureJson ? (
                  <button
                    key="act"
                    type="button"
                    className="compact-button secondary"
                    disabled={isMigrating || batchMigrating}
                    onClick={() => void handleMigrateSingle(defect.id)}
                    style={{
                      fontSize: '11px',
                      fontWeight: 700,
                      color: '#0284c7',
                      borderColor: '#bae6fd',
                      display: 'flex',
                      alignItems: 'center',
                      gap: '4px',
                    }}
                  >
                    {isMigrating ? '⏳ Offloading...' : '🚀 Offload'}
                  </button>
                ) : (
                  <span key="act" style={{ fontSize: '11px', color: '#16a34a', fontWeight: 600 }}>✓ Migrated</span>
                ),
              ],
            }
          })}
        />
        {defects.length === 0 && !searchingDefects && (
          <div style={{ textAlign: 'center', padding: '24px', color: '#64748b', fontSize: '13px' }}>
            No defects found matching the query criteria.
          </div>
        )}
      </section>

      {/* Section 3: SeaweedFS Stored Objects Explorer */}
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">RAW OBJECT BUCKET REPOSITORY</p>
            <h2 style={{ fontSize: '18px', margin: 0 }}>Stored Objects in SeaweedFS</h2>
          </div>
          <div className="actions">
            <button
              type="button"
              className="secondary"
              onClick={() => {
                setObjectName(`payload-${new Date().toISOString().replace(/[:.]/g, '-')}.json`)
                setJsonOpen(true)
              }}
            >
              Upload JSON
            </button>
            <button type="button" onClick={() => setImageOpen(true)}>
              Upload Image
            </button>
          </div>
        </div>

        <DataTable
          columns={['Bucket', 'Object', 'Type', 'Size', 'Backend', 'Created', 'URI', 'Actions']}
          rows={paged.items.map((object) => ({
            id: object.id,
            cells: [
              object.bucket,
              object.objectName,
              object.contentType,
              formatBytes(object.sizeBytes),
              object.backend,
              formatDate(object.createdAtUtc),
              object.uri,
            ],
          }))}
        />
        <Pagination page={paged.page} totalPages={paged.totalPages} totalCount={paged.totalCount} onPage={setPage} />
      </section>

      {/* Modal: Batch Migration */}
      <Modal
        title="Batch Offload Defect JSON to SeaweedFS"
        description="Extract raw FeatureJson and CustomFeatureJson from MariaDB, upload to SeaweedFS, and purge raw DB payloads."
        open={batchModalOpen}
        onClose={() => setBatchModalOpen(false)}
      >
        <div style={{ display: 'grid', gap: '14px' }}>
          <p style={{ fontSize: '13px', color: '#475569', lineHeight: 1.5 }}>
            This process reads defects having inline JSON in MariaDB, uploads each payload to SeaweedFS under{' '}
            <code style={{ background: '#f1f5f9', padding: '2px 6px', borderRadius: '4px' }}>
              {config.defaultBucket}/defects/{'{keyId}'}/{'{defectId}'}_feature.json
            </code>
            , updates defect URLs, and frees space in the database.
          </p>

          <Field label="Batch Processing Limit">
            <select value={batchLimit} onChange={(e) => setBatchLimit(Number(e.target.value))}>
              <option value={10}>10 defects per run</option>
              <option value={25}>25 defects per run</option>
              <option value={50}>50 defects per run</option>
              <option value={100}>100 defects per run</option>
              <option value={500}>500 defects per run</option>
            </select>
          </Field>

          <div
            style={{
              background: '#f0fdf4',
              border: '1px solid #bbf7d0',
              borderRadius: '8px',
              padding: '10px 14px',
              fontSize: '12px',
              color: '#166534',
            }}
          >
            Currently, there are approximately <strong>{inlineDefectsCount}</strong> defects with inline JSON in MariaDB.
          </div>

          <div className="actions" style={{ justifyContent: 'flex-end', marginTop: '10px' }}>
            <button
              type="button"
              className="ghost-button"
              onClick={() => setBatchModalOpen(false)}
              disabled={batchMigrating}
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={batchMigrating}
              onClick={() => void handleMigrateBatch(true)}
              style={{ background: '#0284c7', borderColor: '#0284c7' }}
            >
              {batchMigrating ? '⚡ Offloading in progress...' : '🚀 Start Batch Offload'}
            </button>
          </div>
        </div>
      </Modal>

      {/* Modal: Product ZIP Bundle Packaging */}
      <Modal
        title="Pack Product Defects into Sharded ZIP Archives"
        description="Prevent inode exhaustion by grouping 10~50 defects & images per product into compressed ZIP bundles."
        open={bundleModalOpen}
        onClose={() => setBundleModalOpen(false)}
      >
        <div style={{ display: 'grid', gap: '14px' }}>
          <div
            style={{
              background: '#f0fdfa',
              border: '1px solid #99f6e4',
              borderRadius: '8px',
              padding: '12px 14px',
              fontSize: '12px',
              color: '#115e59',
              lineHeight: 1.5,
            }}
          >
            <strong>🛡️ Inode & Deep Compression Optimization:</strong>
            <br />
            Defects belonging to the same product key are assembled into a single ZIP archive:{' '}
            <code style={{ background: '#ccfbf1', padding: '2px 4px', borderRadius: '3px' }}>
              /defects/XX/YY/ZZ/WW/{'{keyId}'}/{'{keyId}'}_bundle.zip
            </code>
            .
            Contains individual feature JSONs, custom JSONs, defect images, and a manifest.
            Decompressed on-the-fly via streaming endpoints.
          </div>

          <Field label="Max Products to Package">
            <select value={bundleLimit} onChange={(e) => setBundleLimit(Number(e.target.value))}>
              <option value={10}>10 Products per run</option>
              <option value={25}>25 Products per run</option>
              <option value={50}>50 Products per run</option>
              <option value={100}>100 Products per run</option>
              <option value={200}>200 Products per run</option>
            </select>
          </Field>

          <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '13px' }}>
            <input
              type="checkbox"
              checked={bundlePurgeInline}
              onChange={(e) => setBundlePurgeInline(e.target.checked)}
            />
            <span>Purge raw JSON from MariaDB after successful ZIP archive storage</span>
          </label>

          <div className="actions" style={{ justifyContent: 'flex-end', marginTop: '10px' }}>
            <button
              type="button"
              className="ghost-button"
              onClick={() => setBundleModalOpen(false)}
              disabled={bundlePackaging}
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={bundlePackaging}
              onClick={() => void handleMigrateBundles()}
              style={{ background: '#0d9488', borderColor: '#0d9488' }}
            >
              {bundlePackaging ? '⏳ Packaging ZIP Archives...' : '📦 Start Bundle Packaging'}
            </button>
          </div>
        </div>
      </Modal>

      {/* Modal: Product Defect Pack Consolidation */}
      <Modal
        title="Consolidate Product Defects into Unified JSON Pack"
        description="Consolidates all defect NG rows for each Product ID into a single unified .json.gz file in SeaweedFS."
        open={packModalOpen}
        onClose={() => setPackModalOpen(false)}
      >
        <div style={{ display: 'grid', gap: '16px', padding: '4px 0' }}>
          <div style={{ background: '#faf5ff', border: '1px solid #e9d5ff', borderRadius: '8px', padding: '12px 14px', fontSize: '13px', color: '#581c87', lineHeight: 1.5 }}>
            <strong style={{ display: 'block', marginBottom: '4px' }}>📦 Unified Defect Pack Strategy</strong>
            Rather than creating separate JSON files for each individual defect, this groups all defects of each Product ID into a single compressed JSON pack:
            <br />
            <code style={{ background: '#f3e8ff', padding: '2px 4px', borderRadius: '3px' }}>
              /defects/XX/YY/ZZ/WW/{'{keyId}'}_defects.json.gz
            </code>
            <br />
            Individual defects reference sub-resource fragments (e.g. <code>#&#123;defectId&#125;</code>) so on-demand queries continue to stream without disruption.
          </div>

          <Field label="Max Products to Consolidate">
            <select value={packLimit} onChange={(e) => setPackLimit(Number(e.target.value))}>
              <option value={10}>10 Products per run</option>
              <option value={25}>25 Products per run</option>
              <option value={50}>50 Products per run (Recommended)</option>
              <option value={100}>100 Products per run</option>
              <option value={200}>200 Products per run</option>
            </select>
          </Field>

          <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '13px' }}>
            <input
              type="checkbox"
              checked={packPurgeInline}
              onChange={(e) => setPackPurgeInline(e.target.checked)}
            />
            <span>
              <strong>Purge inline MariaDB JSON after successful pack upload</strong> (frees database disk space)
            </span>
          </label>

          <div className="actions" style={{ justifyContent: 'flex-end', gap: '8px', marginTop: '8px' }}>
            <button
              type="button"
              className="ghost-button"
              onClick={() => setPackModalOpen(false)}
              disabled={packConsolidating}
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={packConsolidating}
              onClick={() => void handleMigratePacks()}
              style={{ background: '#7e22ce', borderColor: '#7e22ce', color: '#fff' }}
            >
              {packConsolidating ? '⏳ Consolidating Defect Packs...' : '📦 Start Defect Pack Consolidation'}
            </button>
          </div>
        </div>
      </Modal>

      {/* Modal: Upload JSON */}
      <Modal title="Upload JSON payload" description="POST /storage/json" open={jsonOpen} onClose={() => setJsonOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void uploadJson(event)}>
          <Field label="Bucket" required>
            <input name="bucket" defaultValue={config.defaultBucket || 'payloads'} required />
          </Field>
          <Field label="Object name" required>
            <input name="objectName" value={objectName} onChange={(event) => setObjectName(event.target.value)} required />
          </Field>
          <Field label="JSON payload" required>
            <textarea name="payload" className="code-textarea" defaultValue='{"source":"console"}' required />
          </Field>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Upload
            </button>
            <button type="button" className="ghost-button" onClick={() => setJsonOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      {/* Modal: Upload Image */}
      <Modal title="Upload image" description="POST /storage/images" open={imageOpen} onClose={() => setImageOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void uploadImage(event)}>
          <Field label="Bucket">
            <input name="bucket" defaultValue="images" />
          </Field>
          <Field label="Image file" required>
            <input name="file" type="file" accept="image/*" required />
          </Field>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Upload
            </button>
            <button type="button" className="ghost-button" onClick={() => setImageOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

    </div>
  )
}
