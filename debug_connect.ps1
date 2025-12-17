
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
try {
    Write-Host "Connecting to http://127.0.0.1:5102/api/Security/public-key..."
    $response = $client.GetAsync("http://127.0.0.1:5102/api/Security/public-key").Result
    Write-Host "Response Status: $($response.StatusCode)"
    $content = $response.Content.ReadAsStringAsync().Result
    Write-Host "Content: $content"
} catch {
    Write-Host "EXCEPTION:"
    Write-Host $_.Exception.ToString()
}
