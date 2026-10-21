import { useState } from 'react'
import { ApiError } from '../api/client'
import { maxUploadBytes, uploadContentTypes, uploadImage, type UploadedImage } from '../api/editor'

/**
 * Uploads an image the teacher picked, after the quick checks the server repeats (type and size). `errorCode` is an
 * API error code for the teacher, e.g. IMAGE_TOO_LARGE.
 */
export function useImageUpload(onUploaded: (image: UploadedImage) => void) {
  const [uploading, setUploading] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const upload = async (file: File) => {
    setErrorCode(null)
    if (!(uploadContentTypes as readonly string[]).includes(file.type)) {
      setErrorCode('IMAGE_TYPE_NOT_ALLOWED')
      return
    }
    if (file.size > maxUploadBytes) {
      setErrorCode('IMAGE_TOO_LARGE')
      return
    }
    setUploading(true)
    try {
      onUploaded(await uploadImage(file))
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
    } finally {
      setUploading(false)
    }
  }

  return { upload, uploading, errorCode }
}
