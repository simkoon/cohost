#!/usr/bin/env bash
# REPO-03 / D23: static checks for the three component workflows (no push to GitHub happens in P0, so this is how the
# path filters, runners, tags, permissions and forbidden commands are guaranteed).
#
# CHECK_WORKFLOWS_DIR (default .github/workflows) lets a test point the check at a modified copy of the workflows.
# The repo-level NuGet-publish checks (IsPackable, .nuspec, GeneratePackageOnBuild) always read the repo itself.
#
# Needs ruby (Psych YAML). Ruby reads the YAML key `on` as boolean true, hence doc['on'] || doc[true].
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

export CHECK_WORKFLOWS_DIR="${CHECK_WORKFLOWS_DIR:-.github/workflows}"

if ! command -v ruby > /dev/null 2>&1; then
  echo "check-workflows: ruby is required" >&2
  exit 1
fi

ruby -ryaml - <<'RUBY'
dir = ENV.fetch('CHECK_WORKFLOWS_DIR')
violations = []
fail = ->(msg) { violations << msg }

COMMON = %w[
  protocol/** docs/PROTOCOL.md CoHost.slnx Directory.Build.props Directory.Packages.props
  global.json dotnet-tools.json .editorconfig
].freeze

SERVER_PATHS = (COMMON + %w[
  server/** server.slnf .github/workflows/server.yml docker-compose.yml .dockerignore .env.example
  scripts/** docs/** *.md LICENSE NOTICE .gitignore .gitattributes
]).freeze

SPECS = {
  'server' => {
    paths: SERVER_PATHS,
    runner: 'ubuntu-latest',
    other_dirs: %w[agent/** client/**],
    run_needles: [
      'dotnet test --solution server.slnf', 'npm ci', 'has-pending-model-changes',
      'check-contract-types.sh', 'check-hygiene.sh', 'check-licenses.sh', 'check-workflows.sh',
      'smoke-compose.sh'
    ]
  },
  'agent' => {
    paths: COMMON + %w[agent/** agent.slnf .github/workflows/agent.yml],
    runner: 'windows-latest',
    other_dirs: %w[server/** client/**],
    run_needles: ['dotnet test --solution agent.slnf', 'check-hygiene.sh']
  },
  'client' => {
    paths: COMMON + %w[client/** client.slnf .github/workflows/client.yml],
    runner: 'windows-latest',
    other_dirs: %w[server/** agent/**],
    run_needles: ['dotnet test --solution client.slnf', 'check-hygiene.sh']
  }
}.freeze

FORBIDDEN = {
  'dotnet pack' => /dotnet\s+pack\b/i,
  'nuget push' => /nuget\s+push\b/i,
  'docker push' => /docker\s+push\b/i,
  'docker/login-action' => %r{docker/login-action}i,
  'npm install' => /npm\s+install\b/i,
  'cache: true' => /cache:\s*true\b/i,
  'pull_request_target' => /pull_request_target/i
}.freeze

texts = {}

SPECS.each do |component, spec|
  file = File.join(dir, "#{component}.yml")
  label = "#{component}.yml"

  # (a) exists and parses
  unless File.file?(file)
    fail.call("#{label}: missing (#{file})")
    next
  end
  text = File.read(file)
  texts[label] = text
  begin
    doc = YAML.load_file(file)
  rescue StandardError => e
    fail.call("#{label}: YAML parse error: #{e.message.lines.first.to_s.strip}")
    next
  end
  unless doc.is_a?(Hash)
    fail.call("#{label}: top level is not a mapping")
    next
  end

  triggers = doc['on'] || doc[true]
  unless triggers.is_a?(Hash)
    fail.call("#{label}: no 'on' trigger mapping")
    next
  end

  pr = triggers['pull_request']
  push = triggers['push']
  pr_paths = pr.is_a?(Hash) ? pr['paths'] : nil
  push_paths = push.is_a?(Hash) ? push['paths'] : nil
  fail.call("#{label}: on.pull_request.paths missing") unless pr_paths.is_a?(Array)
  fail.call("#{label}: on.push.paths missing") unless push_paths.is_a?(Array)

  # (b) path lists identical and complete
  if pr_paths.is_a?(Array) && push_paths.is_a?(Array)
    if pr_paths.sort != push_paths.sort
      fail.call("#{label}: pull_request.paths and push.paths differ")
    end
    missing = spec[:paths] - pr_paths
    fail.call("#{label}: paths missing #{missing.join(', ')}") unless missing.empty?
    # (c) no other component's directory
    leaked = spec[:other_dirs] & (pr_paths + push_paths)
    fail.call("#{label}: paths include another component: #{leaked.uniq.join(', ')}") unless leaked.empty?
  end

  # (d) branches and tags
  if push.is_a?(Hash)
    fail.call("#{label}: push.branches must be [main]") unless push['branches'] == ['main']
    fail.call("#{label}: push.tags must be [#{component}-v*]") unless push['tags'] == ["#{component}-v*"]
  end

  # (e) runners
  jobs = doc['jobs']
  if jobs.is_a?(Hash) && !jobs.empty?
    jobs.each do |name, job|
      runner = job.is_a?(Hash) ? job['runs-on'] : nil
      unless runner == spec[:runner]
        fail.call("#{label}: job #{name} runs-on #{runner.inspect}, expected #{spec[:runner]}")
      end
    end
  else
    fail.call("#{label}: no jobs")
    jobs = {}
  end

  # (f) token permissions
  unless doc['permissions'] == { 'contents' => 'read' }
    fail.call("#{label}: top-level permissions must be exactly {contents: read}")
  end

  # (g) required commands and setup-dotnet cache
  steps = jobs.values.flat_map { |j| j.is_a?(Hash) && j['steps'].is_a?(Array) ? j['steps'] : [] }
  runs = steps.map { |s| s.is_a?(Hash) ? s['run'].to_s : '' }
  spec[:run_needles].each do |needle|
    fail.call("#{label}: no run step contains '#{needle}'") unless runs.any? { |r| r.include?(needle) }
  end
  steps.each do |s|
    next unless s.is_a?(Hash) && s['uses'].to_s.start_with?('actions/setup-dotnet')
    cache = (s['with'] || {})['cache']
    fail.call("#{label}: setup-dotnet must not enable cache") if cache && cache.to_s != 'false'
  end
end

# (h) forbidden text anywhere in the workflow files
texts.each do |label, text|
  FORBIDDEN.each do |name, re|
    fail.call("#{label}: forbidden '#{name}'") if text.match?(re)
  end
end

# (i) repo level: nothing is packable or published (D23)
props = File.file?('Directory.Build.props') ? File.read('Directory.Build.props') : ''
unless props =~ %r{<IsPackable>\s*false\s*</IsPackable>}
  fail.call('Directory.Build.props: <IsPackable>false</IsPackable> missing')
end

tracked = `git ls-files --cached --others --exclude-standard`.split("\n")
nuspecs = tracked.select { |f| f.end_with?('.nuspec') && File.file?(f) }
fail.call("tracked .nuspec files: #{nuspecs.join(', ')}") unless nuspecs.empty?

tracked.select { |f| f =~ /\.(csproj|props|targets)\z/ && File.file?(f) }.each do |f|
  if File.read(f) =~ %r{<GeneratePackageOnBuild>\s*true\s*</GeneratePackageOnBuild>}i
    fail.call("#{f}: GeneratePackageOnBuild is true")
  end
end

if violations.empty?
  puts 'check-workflows: OK'
else
  violations.each { |v| warn "check-workflows: #{v}" }
  exit 1
end
RUBY
