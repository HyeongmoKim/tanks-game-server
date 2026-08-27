$SecureDbPassword = Read-Host "tanks_app 비밀번호 입려" -AsSecureString
$DbPassword = [System.Net.NetworkCredential]::new("", $SecureDbPassword).Password

docker run --rm --name tanks-server-local `
  -p 7777:7777 `
  -e "TANKS_DB_CONNECTION_STRING=Host=host.docker.internal;Port=5432;Database=tanks_game;Username=tanks_app;Password=$DbPassword;SSL Mode=Disable" `
  tanks-server:local