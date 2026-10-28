# School Network Manager - remove the agent (run as Administrator)
Unregister-ScheduledTask -TaskName 'SchoolNetworkManager Agent' -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'SchoolNetworkManager Screen' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $env:ProgramData 'SchoolNetworkManager\Agent') -Recurse -Force -ErrorAction SilentlyContinue
Write-Output 'The agent was removed.'
