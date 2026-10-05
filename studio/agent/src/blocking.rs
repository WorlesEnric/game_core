//! Service futures with synchronous SQLite/CAS work run on blocking pool threads, leaving
//! the network/timer executor responsive. Cancellation drops the future on that thread.
use std::future::Future;

struct CancelOnDrop(Option<tokio::sync::oneshot::Sender<()>>);
impl Drop for CancelOnDrop {
    fn drop(&mut self) {
        if let Some(sender) = self.0.take() {
            let _ = sender.send(());
        }
    }
}

/// Spawn a cancellable service lane. Synchronous I/O completes before cancellation, while
/// asynchronous network waits can be cancelled immediately. No mutex is held across await.
pub fn spawn<F>(future: F) -> tokio::task::JoinHandle<F::Output>
where
    F: Future + Send + 'static,
    F::Output: Send + 'static,
{
    let runtime = tokio::runtime::Handle::current();
    tokio::spawn(async move {
        let (sender, receiver) = tokio::sync::oneshot::channel();
        let _cancel = CancelOnDrop(Some(sender));
        let work = tokio::task::spawn_blocking(move || {
            runtime.block_on(async move {
                tokio::select! { output = future => Some(output), _ = receiver => None }
            })
        });
        match work.await {
            Ok(Some(output)) => output,
            Ok(None) => panic!("cancelled service lane resumed"),
            Err(error) => std::panic::resume_unwind(error.into_panic()),
        }
    })
}

#[cfg(test)]
mod tests {
    #[tokio::test(flavor = "current_thread")]
    async fn r2_27_slow_storage_does_not_stall_executor() {
        let job = super::spawn(async {
            std::thread::sleep(std::time::Duration::from_millis(200));
        });
        let start = std::time::Instant::now();
        tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        assert!(start.elapsed() < std::time::Duration::from_millis(150));
        job.await.unwrap();
        let waiting = super::spawn(std::future::pending::<()>());
        waiting.abort();
        assert!(waiting.await.unwrap_err().is_cancelled());
    }
}
