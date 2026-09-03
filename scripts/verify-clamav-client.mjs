import process from 'node:process'
import { randomUUID } from 'node:crypto'

const baseUrl = process.env.OA_TEST_API_BASE
const mode = process.argv[2]
if (!baseUrl || !['scan', 'unavailable'].includes(mode)) {
  throw new Error('Usage: OA_TEST_API_BASE=http://127.0.0.1:<port>/api/v1 node scripts/verify-clamav-client.mjs <scan|unavailable>')
}

const loginResponse = await fetch(`${baseUrl}/auth/login`, {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ userId: 'u-zhang', password: 'Oa@123456' })
})
const loginBody = await loginResponse.json()
const accessToken = loginBody.session?.accessToken
if (loginResponse.status !== 200 || !accessToken) throw new Error(`Demo login failed with HTTP ${loginResponse.status}.`)

async function upload(name, bytes) {
  const form = new FormData()
  form.append('file', new Blob([bytes]), name)
  const response = await fetch(`${baseUrl}/files`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${accessToken}`,
      'idempotency-key': randomUUID()
    },
    body: form
  })
  const body = await response.json().catch(() => ({}))
  return { status: response.status, code: body.code ?? null, stored: Boolean(body.id) }
}

function crc32(bytes) {
  let crc = 0xffffffff
  for (const byte of bytes) {
    crc ^= byte
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1))
  }
  return (crc ^ 0xffffffff) >>> 0
}

function createStoredZip(entries) {
  const localParts = []
  const centralParts = []
  let offset = 0
  for (const [name, content] of entries) {
    const nameBytes = Buffer.from(name)
    const data = Buffer.from(content)
    const checksum = crc32(data)
    const local = Buffer.alloc(30)
    local.writeUInt32LE(0x04034b50, 0)
    local.writeUInt16LE(20, 4)
    local.writeUInt32LE(checksum, 14)
    local.writeUInt32LE(data.length, 18)
    local.writeUInt32LE(data.length, 22)
    local.writeUInt16LE(nameBytes.length, 26)
    localParts.push(local, nameBytes, data)

    const central = Buffer.alloc(46)
    central.writeUInt32LE(0x02014b50, 0)
    central.writeUInt16LE(20, 4)
    central.writeUInt16LE(20, 6)
    central.writeUInt32LE(checksum, 16)
    central.writeUInt32LE(data.length, 20)
    central.writeUInt32LE(data.length, 24)
    central.writeUInt16LE(nameBytes.length, 28)
    central.writeUInt32LE(offset, 42)
    centralParts.push(central, nameBytes)
    offset += local.length + nameBytes.length + data.length
  }

  const centralDirectory = Buffer.concat(centralParts)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(entries.length, 8)
  end.writeUInt16LE(entries.length, 10)
  end.writeUInt32LE(centralDirectory.length, 12)
  end.writeUInt32LE(offset, 16)
  return Buffer.concat([...localParts, centralDirectory, end])
}

if (mode === 'scan') {
  const safe = await upload('safe-clamav-check.pdf', Buffer.from('%PDF-1.4\n%%EOF\n'))
  const eicar = Buffer.from('X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*')
  const infectedDocument = createStoredZip([
    ['[Content_Types].xml', Buffer.from('<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>')],
    ['word/eicar.com', eicar]
  ])
  const infected = await upload('eicar-clamav-check.docx', infectedDocument)
  if (safe.status !== 201 || !safe.stored) throw new Error(`Safe upload failed: ${JSON.stringify(safe)}`)
  if (infected.status !== 400 || infected.code !== 'FILE_006' || infected.stored) throw new Error(`EICAR was not blocked correctly: ${JSON.stringify(infected)}`)
  console.log(JSON.stringify({ safe, infected }))
} else {
  const unavailable = await upload('unavailable-clamav-check.pdf', Buffer.from('%PDF-1.4\n%%EOF\n'))
  if (unavailable.status !== 503 || unavailable.code !== 'FILE_007' || unavailable.stored) throw new Error(`Unavailable scanner did not fail closed: ${JSON.stringify(unavailable)}`)
  console.log(JSON.stringify({ unavailable }))
}
