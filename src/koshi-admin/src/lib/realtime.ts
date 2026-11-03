import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { getAccessToken } from './api'

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

/**
 * Subscribes to device change signals from /hubs/monitoring and invalidates the given query keys.
 * The hub only sends device ids; data is always re-read through permission-checked REST endpoints.
 */
export function useMonitoringRealtime(queryKeys: string[][]): RealtimeStatus {
  const qc = useQueryClient()
  const [status, setStatus] = useState<RealtimeStatus>('connecting')
  const keysJson = JSON.stringify(queryKeys)

  useEffect(() => {
    const keys = JSON.parse(keysJson) as string[][]
    let pending: number | undefined
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/monitoring', { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('deviceChanged', () => {
      // Coalesce bursts of heartbeats into one refetch.
      window.clearTimeout(pending)
      pending = window.setTimeout(() => keys.forEach((k) => qc.invalidateQueries({ queryKey: k })), 400)
    })
    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => setStatus('connected'))
    connection.onclose(() => setStatus('disconnected'))

    connection
      .start()
      .then(() => setStatus(connection.state === HubConnectionState.Connected ? 'connected' : 'disconnected'))
      .catch(() => setStatus('disconnected'))

    return () => {
      window.clearTimeout(pending)
      void connection.stop()
    }
  }, [qc, keysJson])

  return status
}
