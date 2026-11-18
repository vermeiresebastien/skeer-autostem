# Read-only interrogation of the LOM seam that is ALREADY running inside Live.
# Sends only get_script_info and get_session_info - both are pure reads in the
# AbletonMCP dispatch table - and never touches the cursor, the window or the set.
$ErrorActionPreference = 'Stop'
$client = New-Object System.Net.Sockets.TcpClient
$client.Connect('127.0.0.1', 9877)
$stream = $client.GetStream()
$stream.ReadTimeout = 4000
$stream.WriteTimeout = 2000

function Send-Read([string]$type) {
    $payload = '{"type":"' + $type + '","params":{}}' + "`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $buffer = New-Object byte[] 65535
    try {
        $read = $stream.Read($buffer, 0, $buffer.Length)
        if ($read -gt 0) { return [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read) }
        return '<closed without a reply>'
    } catch {
        return '<read failed: ' + $_.Exception.Message + '>'
    }
}

'=== get_script_info ==='
Send-Read 'get_script_info'
'=== get_session_info ==='
Send-Read 'get_session_info'
$client.Close()
'seam closed'
